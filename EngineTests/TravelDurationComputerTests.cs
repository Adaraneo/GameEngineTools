// TravelDurationComputerTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Movement;

namespace EngineTests;

[TestClass]
public class TravelDurationComputerTests
{
    [TestMethod]
    public void ComputeMinutes_FullSpeed_ReturnsExpected()
    {
        var result = TravelDurationComputer.ComputeMinutes(400.0, 80.0);
        Assert.AreEqual(5.0, result, 0.001);
    }

    [TestMethod]
    public void ComputeMinutes_ZeroSpeed_ReturnsMaxValue()
    {
        var result = TravelDurationComputer.ComputeMinutes(100.0, 0.0);
        Assert.AreEqual(double.MaxValue, result);
    }

    [TestMethod]
    public void ComputeMinutes_NegativeSpeed_ReturnsMaxValue()
    {
        var result = TravelDurationComputer.ComputeMinutes(100.0, -1.0);
        Assert.AreEqual(double.MaxValue, result);
    }
}
