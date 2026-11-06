using Lertaro.App.ViewModels.Onboarding;

namespace Lertaro.App.Tests.ViewModels.Onboarding;

[TestClass]
public sealed class OnboardingMachineTests
{
    [TestMethod]
    public void PracticeObserved_AdvancesThroughEveryStep_ThenCompletes()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < OnboardingMachine.StepCount; step++)
        {
            Assert.AreEqual(step, machine.StepIndex);
            Assert.AreEqual(TourOutcome.InProgress, machine.Outcome);
            machine.Handle(TourEvent.PracticeObserved);
        }
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);
        Assert.AreEqual(OnboardingMachine.StepCount, machine.CompletedSteps);
    }

    [TestMethod]
    public void MainWindowShown_Minimizes_AndHidden_Restores()
    {
        var machine = new OnboardingMachine();
        machine.Handle(TourEvent.MainWindowShown);
        Assert.AreEqual(TourMode.Minimized, machine.Mode);
        Assert.AreEqual(0, machine.StepIndex); // the yield does not advance practice

        machine.Handle(TourEvent.MainWindowHidden);
        Assert.AreEqual(TourMode.Expanded, machine.Mode);
    }

    [TestMethod]
    public void Timeout_Folds_AndRestoreReopens_WithoutLosingStep()
    {
        var machine = new OnboardingMachine();
        machine.Handle(TourEvent.PracticeObserved); // step 1
        machine.Handle(TourEvent.Timeout);
        Assert.AreEqual(TourMode.Minimized, machine.Mode);
        Assert.AreEqual(1, machine.StepIndex);

        machine.Handle(TourEvent.Restore);
        Assert.AreEqual(TourMode.Expanded, machine.Mode);
        Assert.AreEqual(1, machine.StepIndex);
    }

    [TestMethod]
    public void SkipRequested_IsTerminal()
    {
        var machine = new OnboardingMachine();
        machine.Handle(TourEvent.SkipRequested);
        Assert.AreEqual(TourOutcome.Skipped, machine.Outcome);

        machine.Handle(TourEvent.PracticeObserved);
        machine.Handle(TourEvent.Restore);
        Assert.AreEqual(TourOutcome.Skipped, machine.Outcome); // nothing revives it
        Assert.AreEqual(0, machine.CompletedSteps);
    }

    [TestMethod]
    public void EventsAfterCompletion_AreIgnored()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < OnboardingMachine.StepCount; step++)
            machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);

        machine.Handle(TourEvent.SkipRequested);
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);
        machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(OnboardingMachine.StepCount, machine.CompletedSteps);
    }

    [TestMethod]
    public void PracticeObserved_LastStep_CompletesDirectly()
    {
        var machine = new OnboardingMachine();
        for (var step = 0; step < OnboardingMachine.StepCount - 1; step++)
            machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(OnboardingMachine.StepCount - 1, machine.StepIndex);
        Assert.AreEqual(TourOutcome.InProgress, machine.Outcome);

        machine.Handle(TourEvent.PracticeObserved);
        Assert.AreEqual(TourOutcome.Completed, machine.Outcome);
    }

    [TestMethod]
    public void FullScenario_YieldWhilePracticing_TimeoutOnStepFour_SkipAtTheEnd()
    {
        // The real first-run shape: wake the window (yields), type and select inside it, the
        // window hides on execution, the last step times out, the user folds it away for good.
        var machine = new OnboardingMachine();

        machine.Handle(TourEvent.MainWindowShown);   // wake -> card minimizes
        machine.Handle(TourEvent.PracticeObserved);  // step 1 done while minimized
        Assert.AreEqual(1, machine.StepIndex);
        machine.Handle(TourEvent.MainWindowHidden);  // window stays up while typing? no -- hidden => expanded
        machine.Handle(TourEvent.PracticeObserved);  // step 2 typed
        machine.Handle(TourEvent.PracticeObserved);  // step 3 selected
        machine.Handle(TourEvent.MainWindowHidden);  // enter executed -> window hides
        machine.Handle(TourEvent.PracticeObserved);  // step 4 done
        Assert.AreEqual(4, machine.StepIndex);

        machine.Handle(TourEvent.Timeout);           // last step ignored for 30s
        machine.Handle(TourEvent.SkipRequested);
        Assert.AreEqual(TourOutcome.Skipped, machine.Outcome);
    }
}
