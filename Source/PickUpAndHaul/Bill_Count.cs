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
			if (onlyMechs && !pawn.IsColonyMech || pawn.inventory == null)
			{
				continue;
			}

			var comp = pawn.HauledToInventoryComp();
			// for normal colonists this wouldn't be needed
			// this touches all pawns in the player faction, which can be put into a very wierd state by other mods
			if (comp == null)
			{
				continue;
			}

			foreach (var thing in pawn.inventory.innerContainer)
			{
				if (!comp.Contains(thing))
				{
					continue;
				}

				var wrappedThing = thing.GetInnerIfMinified();
				if (!worker.CountValidThing(wrappedThing, bill, thingDef))
				{
					continue;
				}

				count += wrappedThing.stackCount;
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
			if (pawn.inventory == null)
			{
				continue;
			}
			var comp = pawn.HauledToInventoryComp();
			if (comp == null)
			{
				continue;
			}
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
