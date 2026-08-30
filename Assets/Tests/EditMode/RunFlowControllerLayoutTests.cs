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
}
