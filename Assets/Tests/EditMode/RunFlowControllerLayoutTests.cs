using NUnit.Framework;

public class RunFlowControllerLayoutTests
{
    [Test]
    public void ComputeStageFloorGap_WiderContainerThanImage_CropsTopAndBottom()
    {
        // Image is 1536x1024 (aspect 1.5). A 1920x1080 container (aspect ~1.778) is wider than the
        // image, so the image scales to container width and crops top/bottom evenly.
        float gap = RunFlowController.ComputeStageFloorGap(1920f, 1080f);

        Assert.Greater(gap, 0f);
    }

    [Test]
    public void ComputeStageFloorGap_NeverReturnsNegative()
    {
        // A container much taller than wide (narrower than image aspect) exercises the other branch.
        float gap = RunFlowController.ComputeStageFloorGap(400f, 2000f);

        Assert.GreaterOrEqual(gap, 0f);
    }

    [Test]
    public void ComputeStageSpriteMarginBottom_SubtractsPaddingSoSpriteSinksToFloor()
    {
        // Прозрачная пустота под ногами кадра поднимает видимый силуэт над нижним краем рамки,
        // поэтому рамку надо ОПУСТИТЬ на эту величину, а не поднять (регрессия «босс парит в
        // воздухе»: 518px рамка Левиафана с отступом 0.27 уезжала вверх на ~140px).
        float margin = RunFlowController.ComputeStageSpriteMarginBottom(200f, 0.25f, 400f);

        Assert.AreEqual(100f, margin, 0.0001f);
    }

    [Test]
    public void ComputeStageSpriteMarginBottom_NoPadding_KeepsFloorGap()
    {
        float margin = RunFlowController.ComputeStageSpriteMarginBottom(200f, 0f, 400f);

        Assert.AreEqual(200f, margin, 0.0001f);
    }

    [Test]
    public void ComputeStageSpriteMarginBottom_PaddingLargerThanGap_ClampsToBottomOfPanel()
    {
        float margin = RunFlowController.ComputeStageSpriteMarginBottom(40f, 0.5f, 518f);

        Assert.AreEqual(0f, margin, 0.0001f);
    }
}
