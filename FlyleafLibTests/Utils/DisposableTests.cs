using AwesomeAssertions;

namespace FlyleafLib;

// Disposable is the anonymous-disposal helper (Create stores an Action invoked once on Dispose; Empty is a no-op
// singleton). It had no coverage. Behaviour is derived directly from the tiny implementation.
public class DisposableTests
{
    [Fact]
    public void Create_InvokesActionOnceOnDispose()
    {
        int count = 0;
        var d = Disposable.Create(() => count++);

        count.Should().Be(0); // not invoked until disposed
        d.Dispose();
        count.Should().Be(1);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        int count = 0;
        var d = Disposable.Create(() => count++);

        d.Dispose();
        d.Dispose(); // action is nulled after the first call

        count.Should().Be(1);
    }

    [Fact]
    public void Empty_DisposeIsNoOp()
    {
        Action act = () => Disposable.Empty.Dispose();
        act.Should().NotThrow();
    }
}
