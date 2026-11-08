using Lertaro.App.ViewModels.Onboarding;

namespace Lertaro.App.Tests.ViewModels.Onboarding;

[TestClass]
public sealed class OnboardingMachineTests
{
    [TestMethod]
    public void PracticeObserved_AdvancesThroughEveryStep_ThenCompletes()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < new OnboardingMachine().StepCount; step++)
        {
            Assert.AreEqual(step, machine.StepIndex);
            Assert.AreEqual(TourOutcome.InProgress, machine.Outcome);
            machine.Handle(TourEvent.PracticeObserved);
        }
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);
        Assert.AreEqual(new OnboardingMachine().StepCount, machine.CompletedSteps);
    }

    [TestMethod]
    public void SkipRequested_IsTerminal()
    {
        var machine = new OnboardingMachine();
        machine.Handle(TourEvent.SkipRequested);
        Assert.AreEqual(TourOutcome.Skipped, machine.Outcome);

        machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(TourOutcome.Skipped, machine.Outcome); // nothing revives it
        Assert.AreEqual(0, machine.CompletedSteps);
    }

    [TestMethod]
    public void EventsAfterCompletion_AreIgnored()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < new OnboardingMachine().StepCount; step++)
            machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);

        machine.Handle(TourEvent.SkipRequested);
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);
        machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(new OnboardingMachine().StepCount, machine.CompletedSteps);
    }

    [TestMethod]
    public void PracticeObserved_LastStep_CompletesDirectly()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < new OnboardingMachine().StepCount - 1; step++)
            machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(new OnboardingMachine().StepCount - 1, machine.StepIndex);
        Assert.AreEqual(TourOutcome.InProgress, machine.Outcome);

        machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);
    }

    [TestMethod]
    public void PersistencePlan_Completed_MarksAndArmsVersion()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < new OnboardingMachine().StepCount; step++)
            machine.Handle(TourEvent.PracticeObserved);

        var (markCompleted, skipStep, updateVersion) = machine.PersistencePlan(1);
        Assert.IsTrue(markCompleted);
        Assert.AreEqual(0, skipStep);
        Assert.IsTrue(updateVersion);
    }

    [TestMethod]
    public void PersistencePlan_Skipped_MarksWithAbandonedStep_KeepsVersion()
    {
        var machine = new OnboardingMachine();
        machine.Handle(TourEvent.PracticeObserved); // finished step one
        machine.Handle(TourEvent.SkipRequested);    // quit on step two

        var (markCompleted, skipStep, updateVersion) = machine.PersistencePlan(1);
        Assert.IsTrue(markCompleted); // one-shot holds: never re-pops by itself
        Assert.AreEqual(2, skipStep); // 1-based: abandoned ON step two
        Assert.IsFalse(updateVersion);
    }

    [TestMethod]
    public void PersistencePlan_InProgress_WritesNothing()
    {
        var machine = new OnboardingMachine();
        machine.Handle(TourEvent.PracticeObserved);

        var (markCompleted, skipStep, updateVersion) = machine.PersistencePlan(1);
        Assert.IsFalse(markCompleted);
        Assert.AreEqual(0, skipStep);
        Assert.IsFalse(updateVersion);
    }
}
