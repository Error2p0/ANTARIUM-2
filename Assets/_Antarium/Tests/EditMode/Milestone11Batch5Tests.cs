using Antarium.Content;
using Antarium.Presentation;
using NUnit.Framework;

namespace Antarium.Tests
{
    /// <summary>
    /// Milestone 11, batch 5: the surface background and falling water are drawn by the terrain shader from
    /// <see cref="RenderConfig"/>; these check the shipped config stays inside what the shader can draw.
    /// </summary>
    public class Milestone11Batch5Tests
    {
        static RenderConfig Shipped()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Antarium/Content/GameConfig.asset");
            Assert.IsNotNull(config, "GameConfig.asset");
            return config.render;
        }

        [Test]
        public void SceneryLayersFitTheShaderFarthestFirst()
        {
            var r = Shipped();
            Assert.IsNotNull(r.sceneryLayers);
            Assert.That(r.sceneryLayers.Length, Is.InRange(1, TerrainView.MaxSceneryLayers), "the shader draws at most this many layers");
            float parallax = -1f;
            foreach (var l in r.sceneryLayers)
            {
                Assert.AreNotEqual(SceneryShape.Off, l.shape);
                Assert.That(l.parallax, Is.GreaterThan(parallax), "farthest first: each layer follows the world more than the one behind it");
                parallax = l.parallax;
                Assert.That(l.width, Is.GreaterThanOrEqualTo(1f));
                if (l.shape == SceneryShape.Peaks && l.snowLine > 0f)
                    Assert.That(l.snowLine, Is.LessThan(l.foot + l.height * 0.7f), "snow below the typical summit, so the caps show");
            }
        }

        [Test]
        public void CloudsAndStreamsHaveSensibleRanges()
        {
            var r = Shipped();
            Assert.That(r.cloudBand.y, Is.GreaterThan(r.cloudBand.x), "the cloud band has a height");
            Assert.That(r.rainCloudCover, Is.GreaterThanOrEqualTo(r.cloudCover), "rain brings more clouds, not fewer");
            Assert.That(r.fallWidth, Is.GreaterThan(0f));
            Assert.That(r.fallStreakLength, Is.GreaterThanOrEqualTo(1f));
            Assert.That(r.fallStreakShare, Is.InRange(0f, 1f));
        }
    }
}
