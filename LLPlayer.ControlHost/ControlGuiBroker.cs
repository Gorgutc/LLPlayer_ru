using LLPlayer.ControlApi;

namespace LLPlayer.ControlHost;

internal sealed class ControlGuiBroker
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private const int MaxSessions = 16;
    private const int MaxCommands = 64;
    private readonly object gate = new();
    private readonly Dictionary<Guid, Session> sessions = new();

    public ControlGuiSession Register(ControlGuiRegistration registration)
    {
        if (registration.ProfileId != ControlProtocol.DefaultProfileId)
            throw new ControlException(new("profile_conflict", "The requested profile is not active."));
        if (string.IsNullOrWhiteSpace(registration.Name) || registration.Name.Length > 120)
            throw new ControlException(new("invalid_arguments", "A name of up to 120 characters is required."));
        if (registration.ProcessId is <= 0 || registration.LaunchId == Guid.Empty ||
            (registration.LaunchId.HasValue && !registration.ProcessId.HasValue) ||
            (registration.LaunchSourceOpened.HasValue && !registration.LaunchId.HasValue))
            throw new ControlException(new("invalid_arguments", "Invalid GUI process registration."));
        lock (gate)
        {
            RemoveExpired();
            if (sessions.Count >= MaxSessions)
                throw new ControlException(new("session_limit", "Too many GUI sessions are active."));
            var id = Guid.NewGuid();
            var token = ControlHostDiscovery.CreateCredential();
            sessions.Add(id, new(registration.Name, token, registration.ProcessId, registration.LaunchId, registration.LaunchSourceOpened));
            return new(id, token);
        }
    }

    public ControlGuiInfo[] List()
    {
        lock (gate)
        {
            RemoveExpired();
            return sessions.Select(pair => new ControlGuiInfo(pair.Key, pair.Value.Name, pair.Value.LastSeen)).ToArray();
        }
    }

    public ControlGuiInfo? ClaimLaunchedSession(int processId, Guid launchId, out bool? sourceOpened)
    {
        lock (gate)
        {
            sourceOpened = null;
            RemoveExpired();
            foreach (var (id, session) in sessions)
            {
                if (session.ProcessId != processId || session.LaunchId != launchId) continue;
                session.LaunchId = null;
                sourceOpened = session.LaunchSourceOpened;
                return new(id, session.Name, session.LastSeen);
            }
            return null;
        }
    }

    public int? RegisteredProcessId(Guid id)
    {
        lock (gate)
        {
            RemoveExpired();
            return sessions.TryGetValue(id, out var session) ? session.ProcessId : null;
        }
    }

    public string? GetUnavailableReason(ControlScope scope)
    {
        lock (gate)
        {
            RemoveExpired();
            if (sessions.Count == 0)
                return "requires_gui";
            if (scope.SessionId is { } id)
                return sessions.ContainsKey(id) ? null : "session_unavailable";
            return sessions.Count == 1 ? null : "session_required";
        }
    }

    public async Task<ControlResult> ExecuteAsync(ControlRequest request, CancellationToken ct)
    {
        Session session;
        Pending pending;
        lock (gate)
        {
            if (GetUnavailableReason(request.Scope) is { } reason)
                return ControlResult.Fail(reason, "Select an active GUI session.");
            var id = request.Scope.SessionId ?? sessions.Keys.Single();
            session = sessions[id];
            if (session.Pending.Count >= MaxCommands)
                return ControlResult.Fail("busy", "The GUI command queue is full.", true);
            var command = new ControlGuiCommand(Guid.NewGuid(), Guid.NewGuid(),
                request with { Scope = request.Scope with { SessionId = id } });
            pending = new(command, session.Queue.AddLast(command));
            session.Pending.Add(command.CommandId, pending);
            session.Signal();
        }

        using var registration = ct.Register(() => RequestCancellation(session, pending));
        return await pending.Completion.Task.ConfigureAwait(false);
    }

    private void RequestCancellation(Session session, Pending pending)
    {
        lock (gate)
        {
            if (pending.Completion.Task.IsCompleted || pending.CancelRequested)
                return;
            pending.CancelRequested = true;
            if (!pending.Delivered)
            {
                session.Queue.Remove(pending.QueueNode);
                session.Pending.Remove(pending.Command.CommandId);
                pending.Completion.TrySetResult(ControlResult.Fail("cancelled", "The GUI command was cancelled before dispatch."));
                session.Signal();
                return;
            }
            session.CancelQueue.Enqueue(pending.Command with { DeliveryId = Guid.NewGuid(), Cancel = true });
            session.Signal();
        }
    }

    public async Task<ControlGuiCommand?> PollAsync(Guid id, ControlGuiPoll poll, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PollTimeout);
        while (true)
        {
            Task signal;
            lock (gate)
            {
                var session = Authorize(id, poll.OwnerToken);
                if (session.InFlight?.DeliveryId == poll.AcknowledgeDeliveryId)
                    session.InFlight = null;
                if (session.InFlight is { } current)
                    return current;

                while (session.CancelQueue.TryDequeue(out var cancel))
                {
                    if (!session.Pending.TryGetValue(cancel.CommandId, out var pending))
                        continue;
                    if (pending.Delivered)
                        return session.InFlight = cancel;
                    session.CancelQueue.Enqueue(cancel);
                    break;
                }
                if (session.Queue.First is { } next)
                {
                    session.Queue.RemoveFirst();
                    var command = next.Value;
                    var pending = session.Pending[command.CommandId];
                    pending.Delivered = true;
                    return session.InFlight = command;
                }
                signal = session.Wake.Task;
            }
            try
            {
                await signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    public void Complete(Guid id, ControlGuiCompletion completion)
    {
        lock (gate)
        {
            var session = Authorize(id, completion.OwnerToken);
            if (!session.Pending.Remove(completion.CommandId, out var pending))
                return;
            if (session.InFlight?.CommandId == completion.CommandId)
                session.InFlight = null;
            pending.Completion.TrySetResult(completion.Result);
            session.Signal();
        }
    }

    public void Detach(Guid id, string ownerToken)
    {
        lock (gate)
        {
            var session = Authorize(id, ownerToken);
            sessions.Remove(id);
            Close(session);
        }
    }

    private Session Authorize(Guid id, string ownerToken)
    {
        RemoveExpired();
        if (!sessions.TryGetValue(id, out var session) ||
            !ControlHostDiscovery.MatchesCredential(session.OwnerToken, ownerToken))
            throw new ControlException(new("invalid_session", "The GUI session is unavailable or unauthorized."));
        session.LastSeen = DateTimeOffset.UtcNow;
        return session;
    }

    public void Expire()
    {
        lock (gate)
            RemoveExpired();
    }

    public void CloseAll()
    {
        lock (gate)
        {
            foreach (var session in sessions.Values)
                Close(session);
            sessions.Clear();
        }
    }

    private void RemoveExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - Lease;
        foreach (var (id, session) in sessions.ToArray())
        {
            if (session.LastSeen >= cutoff)
                continue;
            sessions.Remove(id);
            Close(session);
        }
    }

    private static void Close(Session session)
    {
        foreach (var pending in session.Pending.Values)
            pending.Completion.TrySetResult(ControlResult.Fail("session_disconnected", "The GUI session disconnected.", true));
        session.Signal();
    }

    private sealed class Session(string name, string ownerToken, int? processId, Guid? launchId, bool? launchSourceOpened)
    {
        public string Name { get; } = name;
        public string OwnerToken { get; } = ownerToken;
        public int? ProcessId { get; } = processId;
        public Guid? LaunchId { get; set; } = launchId;
        public bool? LaunchSourceOpened { get; } = launchSourceOpened;
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<Guid, Pending> Pending { get; } = new();
        public LinkedList<ControlGuiCommand> Queue { get; } = new();
        public Queue<ControlGuiCommand> CancelQueue { get; } = new();
        public ControlGuiCommand? InFlight { get; set; }
        public TaskCompletionSource Wake { get; private set; } = NewEvent();

        public void Signal()
        {
            var current = Wake;
            Wake = NewEvent();
            current.TrySetResult();
        }

        private static TaskCompletionSource NewEvent() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Pending(ControlGuiCommand command, LinkedListNode<ControlGuiCommand> queueNode)
    {
        public ControlGuiCommand Command { get; } = command;
        public LinkedListNode<ControlGuiCommand> QueueNode { get; } = queueNode;
        public bool CancelRequested { get; set; }
        public bool Delivered { get; set; }
        public TaskCompletionSource<ControlResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
