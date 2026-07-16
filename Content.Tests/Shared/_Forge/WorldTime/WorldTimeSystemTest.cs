using System;
using Content.Shared._Forge.WorldTime;
using NUnit.Framework;

namespace Content.Tests.Shared._Forge.WorldTime;

[TestFixture]
[TestOf(typeof(WorldTimeSystem))]
public sealed class WorldTimeSystemTest
{
    [TestCase(0f, "00:00")]
    [TestCase(0.2f, "04:48")]
    [TestCase(0.25f, "06:00")]
    [TestCase(0.5f, "12:00")]
    [TestCase(0.75f, "18:00")]
    [TestCase(1f, "00:00")]
    [TestCase(1.5f, "12:00")]
    [TestCase(-0.25f, "18:00")]
    public void CyclePhaseConvertsToExpectedTime(float phase, string expected)
    {
        var timeOfDay = WorldTimeSystem.CyclePhaseToTimeOfDay(phase);

        Assert.That(WorldTimeSystem.FormatTimeOfDay(timeOfDay), Is.EqualTo(expected));
    }

    [Test]
    public void NonFinitePhaseFallsBackToMidnight()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorldTimeSystem.CyclePhaseToTimeOfDay(float.NaN), Is.EqualTo(TimeSpan.Zero));
            Assert.That(WorldTimeSystem.CyclePhaseToTimeOfDay(float.PositiveInfinity), Is.EqualTo(TimeSpan.Zero));
            Assert.That(WorldTimeSystem.CyclePhaseToTimeOfDay(float.NegativeInfinity), Is.EqualTo(TimeSpan.Zero));
        });
    }
}
