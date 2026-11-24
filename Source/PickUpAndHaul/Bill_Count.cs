namespace PickUpAndHaul;

public static class Bill_Count
{
	public static int AddCountForPending(bool onlyMechs, RecipeWorkerCounter worker, Bill_Production bill)
	{
		if (!Settings.StashOnRecipeFinish)
		{
			return 0;
		}

		var count = 0;
		var thingDef = worker.recipe.products[0].thingDef;
		foreach (var pawn in bill.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
		{
			if (onlyMechs && !pawn.IsColonyMech)
			{
				continue;
			}

			var comp = pawn.HauledToInventoryComp();
			foreach (var thing in pawn.inventory.innerContainer)
			{
				if (comp.Contains(thing) && worker.CountValidThing(thing.GetInnerIfMinified(), bill, thingDef))
				{
					count += thing.stackCount;
				}
			}
		}
		return count;
	}

	public static int AddCountForPendingByThingDefs(Bill_Production bill, List<ThingDef> thingDefs)
	{
		if (!Settings.StashOnRecipeFinish)
		{
			return 0;
		}

		var count = 0;
		foreach (var pawn in bill.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
		{
			var comp = pawn.HauledToInventoryComp();
			foreach (var thing in pawn.inventory.innerContainer)
			{
				if (comp.Contains(thing))
				{
					count += thing.stackCount;
				}
			}
		}
		return count;
	}
}
