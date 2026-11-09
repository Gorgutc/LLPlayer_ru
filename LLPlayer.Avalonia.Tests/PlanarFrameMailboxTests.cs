using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public class PlanarFrameMailboxTests
{
    static VideoPresentationParameters Parameters => new(CropRect.Empty, false, false, false, ColorSpace.Bt709, ColorRange.Limited);

    [Fact]
    public void Copies_odd_padded_bottom_up_planes_before_return_and_reuses_bounded_slots()
    {
        PlanarFrameMailbox mailbox = new();
        byte[] y = [4, 5, 6, 99, 1, 2, 3], u = [8, 9], v = [10, 11];
        var frame = new PlanarVideoFrame(PlanarVideoPixelFormat.Yuv420P, 3, 2,
            new(y, 3, 2, 3, 4, true), new(u, 2, 1, 2, 2, false), new(v, 2, 1, 2, 2, false));
        mailbox.Publish(frame, Parameters).Should().BeTrue();
        y[4] = 200;
        var owned = mailbox.Acquire();
        owned.Should().NotBeNull();
        owned!.Y.Take(6).Should().Equal(1, 2, 3, 4, 5, 6);
        mailbox.Release(owned);
        for (int i = 0; i < 100; i++)
        {
            mailbox.Publish(frame, Parameters).Should().BeTrue();
            var next = mailbox.Acquire();
            next.Should().NotBeNull();
            mailbox.Release(next!);
        }
        mailbox.BufferAllocations.Should().BeLessThanOrEqualTo(9);
    }

    [Fact]
    public void Latest_wins_while_acquired_slot_is_immutable_and_clear_invalidates_it()
    {
        PlanarFrameMailbox mailbox = new();
        byte[] y = [1, 2, 3, 4], uv = [128, 128];
        var frame = new PlanarVideoFrame(PlanarVideoPixelFormat.Nv12, 2, 2,
            new(y, 2, 2, 2, 2, false), new(uv, 1, 1, 2, 2, false), default);
        mailbox.Publish(frame, Parameters).Should().BeTrue();
        var held = mailbox.Acquire()!;
        for (int i = 2; i < 30; i++)
        {
            y[0] = (byte)i;
            mailbox.Publish(frame, Parameters).Should().BeTrue();
        }
        held.Y[0].Should().Be(1);
        var latest = mailbox.Acquire()!;
        latest.Y[0].Should().Be(29);
        mailbox.Clear();
        mailbox.IsCurrent(held).Should().BeFalse();
        mailbox.IsCurrent(latest).Should().BeFalse();
        mailbox.Acquire().Should().BeNull();
        mailbox.Release(held);
        mailbox.Release(latest);
    }

    [Fact]
    public void Rejects_truncated_planes_without_replacing_latest_valid_frame()
    {
        PlanarFrameMailbox mailbox = new();
        var good = new PlanarVideoFrame(PlanarVideoPixelFormat.Nv12, 3, 3,
            new(new byte[9], 3, 3, 3, 3, false), new(new byte[8], 2, 2, 4, 4, false), default);
        mailbox.Publish(good, Parameters).Should().BeTrue();
        var bad = new PlanarVideoFrame(PlanarVideoPixelFormat.Nv12, 3, 3,
            new(new byte[8], 3, 3, 3, 3, false), good.Plane1, default);
        mailbox.Publish(bad, Parameters).Should().BeFalse();
        mailbox.Acquire().Should().NotBeNull();
    }
}
