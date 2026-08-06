namespace TheDiscDb.Web.Data;

using System.Collections.Generic;
using System.Linq;
using TheDiscDb;

public static class PartialStateLifecycle
{
    public static PartialState CreateAutomaticUnidentified() => new()
    {
        Type = PartialStateType.Unidentified,
        Reason = PartialStateReason.NeedsIdentification,
        Source = PartialStateSource.Automatic,
    };

    public static bool IsAutomaticUnidentified(PartialState? partial) =>
        partial is
        {
            Type: PartialStateType.Unidentified,
            Reason: PartialStateReason.NeedsIdentification,
            Source: PartialStateSource.Automatic,
        };

    public static void ClearAutomaticUnidentified(IEnumerable<UserContributionDisc> discs)
    {
        foreach (var disc in discs.Where(d => d.Items.Any() && IsAutomaticUnidentified(d.Partial)))
        {
            disc.Partial = null;
        }
    }
}
