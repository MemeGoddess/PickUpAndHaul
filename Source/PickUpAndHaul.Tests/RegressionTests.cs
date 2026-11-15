using DevTools.Testing;
using DevTools.Testing.DevExtensions;
using DevTools.Testing.Utils;
using HarmonyLib;
using RimWorld;
using System.Collections;
using System.Reflection;
using Verse;

namespace PickUpAndHaul.Tests;

[TestFixture(TestType.Playing)]
public class RegressionTests
{
	private Zone_Stockpile stockpile;
	private List<Thing> cleanup = new();
    private static FieldInfo CurTimeSpeed = AccessTools.Field(typeof(TickManager), "curTimeSpeed");
	[OneTimeSetUp]
	public void OneTimeSetup()
	{ 
		CurTimeSpeed.SetValue(Find.TickManager, TimeSpeed.Superfast);
	}

	[OneTimeTearDown]
	public void OneTimeTearDown()
	{
		Tweaker.SuperFast = false;
	}

	[TearDown]
	public void TearDown()
	{
		var list = Find.CurrentMap.mapPawns.AllPawnsSpawned.ToList();
		foreach (var pawn in list)
		{
			pawn.Destroy();
		}

		foreach (var thing in cleanup)
		{
			thing.Destroy();
		}
		cleanup.Clear();
		stockpile?.Delete();
		stockpile = null;
	}
	
	[Test]
	public IEnumerator HaulingInterruptionShouldReturnToHauling()
	{
		var map = Find.CurrentMap;
		var center = map.Center;
		var woodLog = ThingDefOf.WoodLog;
		var unloadJob = PickUpAndHaulJobDefOf.UnloadYourHauledInventory;
		var haulJob = PickUpAndHaulJobDefOf.HaulToInventory;

		var stockpileOffset = new IntVec3(10, 0, -4);
		stockpile = Find.CurrentMap.DEV_CreateStockpileFromCells([center - stockpileOffset, center - stockpileOffset + new IntVec3(1, 0, 0)]);
		var shelf = (Building_Storage)ThingMaker.MakeThing(ThingDefOf.Shelf, GenStuff.DefaultStuffFor(ThingDefOf.Shelf));
		GenSpawn.Spawn(shelf, center - stockpileOffset - new IntVec3(4, 0, 0), map);
		shelf.SetFaction(Faction.OfPlayer);
		shelf.GetStoreSettings().Priority = StoragePriority.Normal;
		cleanup.Add(shelf);
		
		// var woodExisting = GenSpawn.Spawn(woodLog, center - stockpileOffset, map);
		
		var overseer = TestPawns.Overseer.Spawn();
		overseer.drafter.Drafted = true;
		Thing wood = new Thing();
		
		var builder = PawnKindDef.Named("Mech_Lifter").AsBuilder().WithPostSpawn((pawn, index) =>
		{
			wood = GenSpawn.Spawn(ThingDefOf.WoodLog, pawn.Position + new IntVec3(1, 0, 0), map);
			wood.stackCount = 10;
			
			overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, pawn);
			pawn.jobs.StopAll();
			cleanup.Add(wood);
		});

		var otherLifter = builder.Spawn();
		yield return new WaitUntilTimeout(() => otherLifter.inventory.Contains(wood));
		
		overseer.Position = otherLifter.Position - new IntVec3(0, 0, 1);
		overseer.Notify_Teleported();
		yield return new WaitTicks(5);
		otherLifter.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5, hitPart: otherLifter.RaceProps.body.corePart));
		var repair = otherLifter.GetComp<CompMechRepairable>();
		repair.autoRepair = true;
		overseer.drafter.Drafted = false;
		var health = otherLifter.health.summaryHealth.SummaryHealthPercent;
		Log.Message("Lifter damaged");
		yield return new WaitUntilTimeout(() => otherLifter.health.summaryHealth.SummaryHealthPercent > health);
		Log.Message("Lifter repaired");
		yield return new WaitUntilTimeout(() =>
		{
			
			var finish = shelf.GetSlotGroup().HeldThingsCount > 0 || stockpile.AllContainedThings.Contains(wood);
			if(!finish && wood.Spawned)
				Test.Fail("Wood was dropped before hauling");
			
			return finish;
		});
		Log.Message("Lifter job finished");
		if (shelf.GetSlotGroup().HeldThingsCount > 0)
		{
			Test.Fail("Items put in the wrong storage spot");
		}
		cleanup.AddRange(shelf.GetSlotGroup().HeldThings);
	}

	[Test]
	public IEnumerator CuttingPlantShouldntUnload()
	{
		var map = Find.CurrentMap;
		var center = map.Center;
		var agrihandDef = PawnKindDef.Named("Mech_Agrihand");
		
		var overseer = PawnKindDefOf.Colonist
			.AsBuilder().WithName("Overseer")
			.WithMech(agrihandDef).Spawn();
		overseer.drafter.Drafted = true;

		var hydroDef = ThingDefOf.HydroponicsBasin;
		var hydro = (Building_PlantGrower)GenSpawn.Spawn(hydroDef, center + new IntVec3(5, 0, 0), map);
		hydro.SetFaction(Faction.OfPlayer);
		(hydro.PowerComp as CompPowerTrader).PowerOn = true;
		
		var stockpileCells = GenRadial.RadialCellsAround(center - new IntVec3(5, 0, 0), 0, 4).ToArray();
		stockpile = map.DEV_CreateStockpileFromCells(stockpileCells);
		var shelf = (Building_Storage)ThingMaker.MakeThing(ThingDefOf.Shelf, GenStuff.DefaultStuffFor(ThingDefOf.Shelf));
		GenSpawn.Spawn(shelf, center -  new IntVec3(5, 0, 0), map);
		shelf.SetFaction(Faction.OfPlayer);

		var cottonPlant = ThingDef.Named("Plant_Cotton");
		var psychoidPlant = ThingDef.Named("Plant_Psychoid");
		
		hydro.SetPlantDefToGrow(cottonPlant);
		var cells = hydro.OccupiedRect();
		yield return new WaitUntilTimeout(() => cells.All(x => x.GetThingList(map).Any(y => y.def == cottonPlant && (y is Plant { sown: true, LifeStage: PlantLifeStage.Growing }) )));
		
		Log.Message("force growth now");
		foreach (var cell in cells)
		{
			var plant = (Plant)cell.GetThingList(map).FirstOrDefault(x => x.def == cottonPlant);
			plant.Growth = 0.9f;
		}
		hydro.SetPlantDefToGrow(psychoidPlant);

		yield return new WaitUntilTimeout(() =>
		{
			if (shelf.GetSlotGroup().HeldThingsCount <= 0)
			{
				return cells.All(x => x.GetFirstThing<Plant>(map)?.def == psychoidPlant);
			}

			Test.Fail("Shelf had items before everything was harvested");
			return true;
		});
		cleanup.AddRange(shelf.GetSlotGroup().HeldThings);
	}
	
	[Test]
	public IEnumerator CuttingPlantShouldntUnloadHuman()
	{
		var map = Find.CurrentMap;
		var center = map.Center;
		
		var hauler = PawnKindDefOf.Colonist
			.AsBuilder().WithName("Hauler")
			.WithWorkMaxed(WorkTypeDefOf.Hauling, WorkTypeDefOf.Growing).Spawn();

		var hydroDef = ThingDefOf.HydroponicsBasin;
		var hydro = (Building_PlantGrower)GenSpawn.Spawn(hydroDef, center + new IntVec3(5, 0, 0), map);
		hydro.SetFaction(Faction.OfPlayer);
		(hydro.PowerComp as CompPowerTrader).PowerOn = true;
		
		var stockpileCells = GenRadial.RadialCellsAround(center - new IntVec3(5, 0, 0), 0, 4).ToArray();
		stockpile = map.DEV_CreateStockpileFromCells(stockpileCells);
		var shelf = (Building_Storage)ThingMaker.MakeThing(ThingDefOf.Shelf, GenStuff.DefaultStuffFor(ThingDefOf.Shelf));
		GenSpawn.Spawn(shelf, center -  new IntVec3(5, 0, 0), map);
		shelf.SetFaction(Faction.OfPlayer);

		var cottonPlant = ThingDef.Named("Plant_Cotton");
		var psychoidPlant = ThingDef.Named("Plant_Psychoid");
		
		hydro.SetPlantDefToGrow(cottonPlant);
		var cells = hydro.OccupiedRect();
		yield return new WaitUntilTimeout(() => cells.All(x => x.GetThingList(map).Any(y => y.def == cottonPlant && (y is Plant { sown: true, LifeStage: PlantLifeStage.Growing }) )));
		
		Log.Message("force growth now");
		foreach (var cell in cells)
		{
			var plant = (Plant)cell.GetThingList(map).FirstOrDefault(x => x.def == cottonPlant);
			plant.Growth = 0.9f;
		}
		hydro.SetPlantDefToGrow(psychoidPlant);

		yield return new WaitUntilTimeout(() =>
		{
			if (shelf.GetSlotGroup().HeldThingsCount <= 0)
			{
				return cells.All(x => x.GetFirstThing<Plant>(map)?.def == psychoidPlant);
			}

			Test.Fail("Shelf had items before everything was harvested");
			return true;
		});
	}
}
