namespace AiAgent.Backend.Services.AgentRuntime;

/// <summary>Single owner of legal run transitions, including future approval/input resume paths.</summary>
public static class RunStateMachine
{
    public static bool CanTransition(TurnRunStatus current, TurnRunStatus next) => (current, next) switch
    {
        (TurnRunStatus.Draft, TurnRunStatus.Queued) => true,
        (TurnRunStatus.Queued, TurnRunStatus.Starting or TurnRunStatus.Cancelled or TurnRunStatus.Expired) => true,
        (TurnRunStatus.Starting, TurnRunStatus.Running or TurnRunStatus.Failed or TurnRunStatus.Cancelled) => true,
        (TurnRunStatus.Running, TurnRunStatus.Completed or TurnRunStatus.Failed or TurnRunStatus.Cancelled or TurnRunStatus.Expired or TurnRunStatus.WaitingApproval or TurnRunStatus.WaitingInput) => true,
        (TurnRunStatus.WaitingApproval, TurnRunStatus.Running or TurnRunStatus.Cancelled or TurnRunStatus.Failed or TurnRunStatus.Expired) => true,
        (TurnRunStatus.WaitingInput, TurnRunStatus.Running or TurnRunStatus.Cancelled or TurnRunStatus.Failed or TurnRunStatus.Expired) => true,
        _ => false
    };
}
