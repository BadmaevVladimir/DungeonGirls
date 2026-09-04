using NUnit.Framework;

public class ChestRevealAnimatorTests
{
    [Test]
    public void ShouldJumpToEnding_StillInBuildup_ReturnsTrue()
    {
        Assert.IsTrue(ChestRevealAnimator.ShouldJumpToEnding(true, 1.2f));
    }

    [Test]
    public void ShouldJumpToEnding_AlreadyPastBuildup_ReturnsFalse()
    {
        Assert.IsFalse(ChestRevealAnimator.ShouldJumpToEnding(true, ChestRevealAnimator.JingleBuildupDuration + 0.1f));
    }

    [Test]
    public void ShouldJumpToEnding_NotPlaying_ReturnsFalse()
    {
        Assert.IsFalse(ChestRevealAnimator.ShouldJumpToEnding(false, 0f));
    }
}
