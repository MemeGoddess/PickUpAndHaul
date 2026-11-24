namespace PickUpAndHaul;

public class FloatMenuOptionProvider_UnloadInventory: FloatMenuOptionProvider
{
	// Publicizer causes these to be public
	public override bool Drafted => false;
	public override bool Undrafted => true;
	public override bool Multiselect => true;
	public override bool RequiresManipulation => true;
	public override bool MechanoidCanDo => true;

	public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
	{
		return base.SelectedPawnValid(pawn, context) && pawn.isAutoUnloadInventoryAllowed() && !(pawn.HauledToInventoryComp()?.IsEmpty() ?? true);
	}

	public override FloatMenuOption GetSingleOption(FloatMenuContext context)
	{
		var pawnsWithPendingUnloads = new HashSet<Pawn>(context.ValidSelectedPawns);
		if (pawnsWithPendingUnloads.Count == 0)
		{
			return null;
		}

		var hasInventoryToUnload = false;
		foreach (var pawn in context.ClickedPawns)
		{
			if (!pawnsWithPendingUnloads.Contains(pawn))
			{
				continue;
			}

			hasInventoryToUnload = true;
			break;
		}
		if (!hasInventoryToUnload)
		{
			return null;
		}

		return new FloatMenuOption("PUAH.UnloadInventory".Translate(), () =>
		{
			// ValidSelectedPawns is not valid in the call back, and the valid state may change between opening the right-click menu and triggering the action
			foreach (var pawn in context.allSelectedPawns)
			{
				if (!SelectedPawnValid(pawn, context))
				{
					continue;
				}

				var job = JobMaker.MakeJob(JobDefOfs.UnloadYourHauledInventory, pawn);
				pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
			}
		}, MenuOptionPriority.High);
	}
}
