using DevTools.Testing;
using DevTools.Testing.DevExtensions;
using DevTools.Testing.Utils;
using RimWorld;
using System.Collections;
using Verse;

namespace PickUpAndHaul.Tests;

[TestFixture(TestType.Playing)]
public class RegressionTests
{
	private const int cellOffset = 4;
	private const int totalCells = 4;
	[Test]
	public IEnumerator HaulingInterruptionShouldReturnToHauling()
	{
		var map = Find.CurrentMap;
		var center = map.Center;
		var woodLog = ThingDefOf.WoodLog;
		var unloadJob = PickUpAndHaulJobDefOf.UnloadYourHauledInventory;
		var haulJob = PickUpAndHaulJobDefOf.HaulToInventory;

		var stockpileOffset = new IntVec3(10, 0, -4);
		var stockpileCells = Enumerable.Range(0, totalCells * cellOffset)
			.Select(index => center - stockpileOffset - new IntVec3(0, 0, index)).ToArray();
		var stockpile = Find.CurrentMap.DEV_CreateStockpileFromCells(stockpileCells);
		
		var woodExisting = GenSpawn.Spawn(woodLog, center - stockpileOffset, map);
		
		var builder = PawnKindDefOf.Colonist.AsBuilder().WithPostSpawn((pawn, index) =>
		{
			pawn.Position += new IntVec3(0, 0, cellOffset * index);
			
			var wood = GenSpawn.Spawn(ThingDefOf.WoodLog, pawn.Position + new IntVec3(1, 0, 0), map);
			wood.stackCount = 10;
		});

		var pawnHauler = builder.WithWorkMaxed(WorkTypeDefOf.Hauling).Spawn();
		yield return pawnHauler.WaitJob(haulJob, waitJobType: WaitJobType.StartsJob);
		var stack = pawnHauler.CurJob.targetA.Thing;
		yield return new WaitUntilTimeout(() => pawnHauler.inventory.Contains(stack));
		var pawnHaulerFullWait = pawnHauler.WaitJob(unloadJob);


		var overseer = TestPawns.Overseer.Spawn();
		overseer.drafter.Drafted = true;
		
		
		// Do more pawns here
		var lifter = builder.WithKind(PawnKindDef.Named("Mech_Lifter")).WithPostSpawn(((pawn, i) =>
		{
			overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, pawn);
			pawn.jobs.StopAll();
		})).Spawn();
		yield return lifter.WaitJob(haulJob, waitJobType: WaitJobType.StartsJob);
		stack = lifter.CurJob.targetA.Thing;
		yield return new WaitUntilTimeout(() => lifter.inventory.Contains(stack));
		var lifterFullWait = lifter.WaitJob(unloadJob);
		
		yield return pawnHaulerFullWait;
		yield return lifterFullWait;

		var otherLifter = builder.Spawn();
		yield return otherLifter.WaitJob(haulJob, waitJobType: WaitJobType.StartsJob);
		stack = otherLifter.CurJob.targetA.Thing;
		yield return new WaitUntilTimeout(() => otherLifter.inventory.Contains(stack));
		var otherLifterFullWait = otherLifter.WaitJob(unloadJob);
		
		overseer.Position = otherLifter.Position - new IntVec3(0, 0, 1);
		yield return new WaitTicks(5);
		otherLifter.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5, hitPart: otherLifter.RaceProps.body.corePart));
		var repair = otherLifter.GetComp<CompMechRepairable>();
		repair.autoRepair = true;
		overseer.drafter.Drafted = false;
		
		yield return otherLifterFullWait;
	}
}
