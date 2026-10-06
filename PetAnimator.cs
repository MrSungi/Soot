using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Soot;

internal enum PetReaction
{
    Wave,
    PettingBlink,
    Pickup,
    Release
}

internal readonly record struct PetPose(BitmapSource Frame, double ScaleX, double ScaleY, double Opacity);

internal sealed class PetAnimator
{
    private static readonly TimeSpan WaveFrameDuration = TimeSpan.FromMilliseconds(155);
    private static readonly TimeSpan PickupDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(520);
    private static readonly TimeSpan WalkFrameDuration = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan PettingBlinkDuration = TimeSpan.FromMilliseconds(600);

    private readonly BitmapSource idleFrame;
    private readonly BitmapSource blinkFrame;
    private readonly BitmapSource[] waveFrames;
    private readonly BitmapSource pickupFrame;
    private readonly BitmapSource releaseFrame;
    private readonly BitmapSource[] rightFrames;
    private readonly BitmapSource[] leftFrames;
    private double elapsed;
    private double untilBlink = NextBlinkDelay();
    private double blinkRemaining;
    private double reactionElapsed;
    private PetReaction? activeReaction;
    private double walkFrameElapsed;
    private int walkFrameIndex;

    internal PetAnimator(BitmapSource sheet)
    {
        idleFrame = Crop(sheet, 0, 0);
        blinkFrame = Crop(sheet, 384, 0);
        waveFrames = Enumerable.Range(0, 4).Select(i => Crop(sheet, i * 192, 624)).ToArray();
        pickupFrame = Crop(sheet, 384, 832);
        releaseFrame = Crop(sheet, 768, 832);
        rightFrames = Enumerable.Range(0, 8).Select(i => Crop(sheet, i * 192, 208)).ToArray();
        leftFrames = Enumerable.Range(0, 8).Select(i => Crop(sheet, i * 192, 416)).ToArray();
    }

    internal void React(PetReaction reaction)
    {
        activeReaction = reaction;
        reactionElapsed = 0;
    }

    internal void CancelReaction(PetReaction? reaction = null)
    {
        if (reaction is not null && activeReaction != reaction) return;
        activeReaction = null;
        reactionElapsed = 0;
    }

    internal PetPose Update(double deltaSeconds, bool walking, bool facingRight, bool enabled)
    {
        var delta = Math.Clamp(deltaSeconds, 0, 0.1);
        elapsed += delta;
        if (!enabled)
        {
            activeReaction = null;
            reactionElapsed = 0;
            return new PetPose(idleFrame, 1, 1, 1);
        }

        if (activeReaction is PetReaction.Pickup && walking && reactionElapsed >= PickupDuration.TotalSeconds)
        {
            activeReaction = null;
            reactionElapsed = 0;
        }

        if (activeReaction is { } reaction)
        {
            var pose = ReactionPose(reaction, reactionElapsed);
            reactionElapsed += delta;
            if (reaction == PetReaction.Pickup && walking)
            {
                if (reactionElapsed >= PickupDuration.TotalSeconds)
                {
                    activeReaction = null;
                    reactionElapsed = 0;
                }
            }
            else if (reactionElapsed >= ReactionDuration(reaction))
            {
                activeReaction = null;
                reactionElapsed = 0;
            }
            return pose;
        }

        if (walking)
        {
            walkFrameElapsed += delta;
            while (walkFrameElapsed >= WalkFrameDuration.TotalSeconds)
            {
                walkFrameElapsed -= WalkFrameDuration.TotalSeconds;
                walkFrameIndex = (walkFrameIndex + 1) % 8;
            }
            var bob = Math.Sin(elapsed * 15) * 0.012;
            return new PetPose((facingRight ? rightFrames : leftFrames)[walkFrameIndex], 1 - bob * 0.35, 1 + bob, 1);
        }

        walkFrameIndex = 0;
        walkFrameElapsed = 0;
        if (blinkRemaining > 0)
        {
            blinkRemaining = Math.Max(0, blinkRemaining - delta);
            return IdlePose(blinkFrame);
        }

        untilBlink -= delta;
        if (untilBlink <= 0)
        {
            blinkRemaining = BlinkDuration.TotalSeconds;
            untilBlink = NextBlinkDelay();
            return IdlePose(blinkFrame);
        }

        return IdlePose(idleFrame);
    }

    private PetPose ReactionPose(PetReaction reaction, double reactionElapsed)
    {
        var duration = ReactionDuration(reaction);
        var progress = Math.Clamp(reactionElapsed / duration, 0, 1);
        var eased = progress * progress * (3 - 2 * progress);
        var settle = Math.Sin(progress * Math.PI);
        var scaleX = 1 - settle * 0.025;
        var scaleY = 1 + settle * 0.045;
        var opacity = 0.9 + eased * 0.1;

        var frame = reaction switch
        {
            PetReaction.Wave => WaveFrameAt(reactionElapsed),
            PetReaction.PettingBlink => blinkFrame,
            PetReaction.Pickup => pickupFrame,
            PetReaction.Release => releaseFrame,
            _ => idleFrame
        };
        return new PetPose(frame, scaleX, scaleY, opacity);
    }

    private BitmapSource WaveFrameAt(double reactionElapsed)
    {
        var frameIndex = Math.Min((int)(reactionElapsed / WaveFrameDuration.TotalSeconds), waveFrames.Length - 1);
        return waveFrames[frameIndex];
    }

    private static double ReactionDuration(PetReaction reaction) => reaction switch
    {
        PetReaction.Wave => WaveFrameDuration.TotalSeconds * 4,
        PetReaction.PettingBlink => PettingBlinkDuration.TotalSeconds,
        PetReaction.Pickup => PickupDuration.TotalSeconds,
        PetReaction.Release => ReleaseDuration.TotalSeconds,
        _ => WaveFrameDuration.TotalSeconds
    };

    private PetPose IdlePose(BitmapSource frame)
    {
        var breath = Math.Sin(elapsed * (2 * Math.PI / 3.8)) * 0.009;
        return new PetPose(frame, 1 - breath * 0.3, 1 + breath, 1);
    }

    private static double NextBlinkDelay() => 4.5 + Random.Shared.NextDouble() * 4.0;

    private static BitmapSource Crop(BitmapSource sheet, int x, int y) =>
        new CroppedBitmap(sheet, new Int32Rect(x, y, 192, 208));
}
