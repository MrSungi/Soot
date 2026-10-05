using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Soot;

internal readonly record struct PetPose(BitmapSource Frame, double ScaleX, double ScaleY);

internal sealed class PetAnimator
{
    private static readonly TimeSpan ReactionDuration = TimeSpan.FromMilliseconds(680);
    private static readonly TimeSpan WalkFrameDuration = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromMilliseconds(125);

    private readonly BitmapSource idleFrame;
    private readonly BitmapSource blinkFrame;
    private readonly BitmapSource clickFrame;
    private readonly BitmapSource[] rightFrames;
    private readonly BitmapSource[] leftFrames;
    private double elapsed;
    private double untilBlink = NextBlinkDelay();
    private double blinkRemaining;
    private double reactionRemaining;
    private double walkFrameElapsed;
    private int walkFrameIndex;

    internal PetAnimator(BitmapSource sheet)
    {
        idleFrame = Crop(sheet, 0, 0);
        blinkFrame = Crop(sheet, 384, 0);
        clickFrame = Crop(sheet, 192, 624);
        rightFrames = Enumerable.Range(0, 8).Select(i => Crop(sheet, i * 192, 208)).ToArray();
        leftFrames = Enumerable.Range(0, 8).Select(i => Crop(sheet, i * 192, 416)).ToArray();
    }

    internal void React() => reactionRemaining = ReactionDuration.TotalSeconds;

    internal PetPose Update(double deltaSeconds, bool walking, bool facingRight, bool enabled)
    {
        var delta = Math.Clamp(deltaSeconds, 0, 0.1);
        elapsed += delta;
        if (!enabled) return new PetPose(idleFrame, 1, 1);

        if (reactionRemaining > 0)
        {
            reactionRemaining = Math.Max(0, reactionRemaining - delta);
            var progress = 1 - reactionRemaining / ReactionDuration.TotalSeconds;
            var bounce = Math.Sin(progress * Math.PI) * 0.085;
            return new PetPose(clickFrame, 1 - bounce * 0.3, 1 + bounce);
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
            return new PetPose((facingRight ? rightFrames : leftFrames)[walkFrameIndex], 1 - bob * 0.35, 1 + bob);
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

    private PetPose IdlePose(BitmapSource frame)
    {
        var breath = Math.Sin(elapsed * (2 * Math.PI / 3.8)) * 0.009;
        return new PetPose(frame, 1 - breath * 0.3, 1 + breath);
    }

    private static double NextBlinkDelay() => 4.5 + Random.Shared.NextDouble() * 4.0;

    private static BitmapSource Crop(BitmapSource sheet, int x, int y) =>
        new CroppedBitmap(sheet, new Int32Rect(x, y, 192, 208));
}
