using DevTools.Testing;
using DevTools.Testing.Utils;
using RimWorld;
using System.Collections;
using UnityEngine.Assertions;
using Verse;

namespace PickUpAndHaul.Tests;

[TestFixture(TestType.Playing)]
public class BillDropStashTests
{
	private Map map;
	private IntVec3 center;

	private Thing smokeleaf, psychoid, steel, shelf;
	private Building_WorkTable drugLab, fabricationBench;
	private RecipeDef componentRecipe, smokeleafJointRecipe, flakeRecipe;

	private Pawn worker;

	[OneTimeSetUp]
	public void OneTimeSetup()
	{
		map = Find.CurrentMap;
		center = map.Center;

		componentRecipe = DefDatabase<RecipeDef>.GetNamed("Make_ComponentIndustrial");
		smokeleafJointRecipe = DefDatabase<RecipeDef>.GetNamed("Make_SmokeleafJoint");
		flakeRecipe = DefDatabase<RecipeDef>.GetNamed("Make_Flake");
		
		
		var researchManager = Current.Game.researchManager;
		foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading) 
			researchManager.FinishProject(project, 
				false, 
				null, 
				false);
	}
	
	[SetUp]
	public void Setup()
	{
		var shelfPosition = center - new IntVec3(7, 0, 0);
		shelf = (Building_Storage)ThingMaker.MakeThing(ThingDefOf.Shelf, GenStuff.DefaultStuffFor(ThingDefOf.Shelf));
		GenSpawn.Spawn(shelf, shelfPosition, map);
		shelf.SetFaction(Faction.OfPlayer);

		smokeleaf = ThingMaker.MakeThing(ThingDef.Named("SmokeleafLeaves"));
		GenSpawn.Spawn(smokeleaf, shelfPosition, map);
		smokeleaf.stackCount = smokeleaf.def.stackLimit;

		psychoid = ThingMaker.MakeThing(ThingDef.Named("PsychoidLeaves"));
		GenSpawn.Spawn(psychoid, shelfPosition, map);
		psychoid.stackCount = psychoid.def.stackLimit;
		
		steel = ThingMaker.MakeThing(ThingDefOf.Steel);
		GenSpawn.Spawn(steel, shelfPosition, map);
		steel.stackCount = steel.def.stackLimit;

		var drugLabPosition = center - new IntVec3(4, 0, -1);
		var drugLabDef = ThingDef.Named("DrugLab");
		drugLab = (Building_WorkTable)ThingMaker.MakeThing(drugLabDef, GenStuff.DefaultStuffFor(drugLabDef));
		GenSpawn.Spawn(drugLab, drugLabPosition, map);
		drugLab.SetFaction(Faction.OfPlayer);
		
		var fabricationBenchPosition = center - new IntVec3(0, 0, -1);
		fabricationBench = (Building_WorkTable)ThingMaker.MakeThing(ThingDef.Named("FabricationBench"));
		GenSpawn.Spawn(fabricationBench, fabricationBenchPosition, map);
		fabricationBench.SetFaction(Faction.OfPlayer);
		var fabricationBenchPowerComp = fabricationBench.TryGetComp<CompPowerTrader>();
		fabricationBenchPowerComp.PowerOn = true;
		
		worker = TestPawns.Worker
			.WithWorkMaxed(WorkTypeDefOf.Smithing, WorkTypeDefOf.Crafting, WorkTypeDefOf.Research)
			.WithStatMultiplier(10f, StatDefOf.WorkSpeedGlobal)
			.Spawn();
	}

	[TearDown]
	public void TearDown()
	{
		smokeleaf.Destroy();
		smokeleaf = null;
		
		psychoid.Destroy();
		psychoid = null;
		
		steel.Destroy();
		steel = null;
		
		foreach (var heldThing in shelf.GetSlotGroup().HeldThings)
		{
			heldThing.Destroy();
		}
		
		shelf.Destroy();
		shelf = null;
		
		fabricationBench.Destroy();
		fabricationBench = null;
		
		drugLab.Destroy();
		drugLab = null;

		worker.Destroy();
	}

	[Test]
	public IEnumerator StashWhenChangingWorkType()
	{
		var componentBill = (Bill_Production)componentRecipe.MakeNewBill();
		componentBill.repeatMode = BillRepeatModeDefOf.RepeatCount;
		componentBill.repeatCount = 1;

		var jointBill = (Bill_Production)smokeleafJointRecipe.MakeNewBill();
		jointBill.repeatMode = BillRepeatModeDefOf.RepeatCount;
		jointBill.repeatCount = 1;
		
		fabricationBench.BillStack.AddBill(componentBill);
		drugLab.BillStack.AddBill(jointBill);

		var jointDef = ThingDefOf.SmokeleafJoint;
		var componentDef = ThingDefOf.ComponentIndustrial;
		yield return new WaitUntilTimeout(() =>
		{
			if (componentBill.repeatCount != 0)
				return false;

			if (jointBill.repeatCount == 0 && worker.inventory.innerContainer.Any(item => item.def == componentDef))
			{
				Test.Fail("Worker had a component still in their inventory after switching worktype");
				return true;
			}

			return jointBill.repeatCount == 0 && componentBill.repeatCount == 0;
		}, GenDate.TicksPerHour * 6);
		
		yield return new WaitUntilTimeout(() => worker.inventory.innerContainer.Count == 0, GenDate.TicksPerHour);
	}

	public MapGenerationSettings MapGenerationSettings
	{
		get
		{
			var mapGen = new MapGenerationSettings
			{
				size = 50,
				biome = BiomeDefOf.TemperateForest,
				mapGeneratorDef = new MapGeneratorDef
				{
					defName = "DevTools_DefaultTestMap",
					genSteps = [
						new GenStepDef
						{
							defName = "DevTools_DefaultGenStep",
							order = 210, // Same as GenStepDef - Terrain
							genStep = new GenStep_EmptyMap()
						},
						DefDatabase<GenStepDef>.GetNamed("FindPlayerStartSpot")
					]
				}
			};
			mapGen.postGenerationActions.Add(ClearMap);
			return mapGen;
		}
	}
	private static void ClearMap()
	{
		// NOTE: Some mods spawn stuff post map-generation with no regard for gen steps. This bypasses
		// our blank map generation which can cause intermittent failures.
		Map map = Find.CurrentMap;
		foreach (IntVec3 cell in CellRect.WholeMap(map))
		{
			map.roofGrid.SetRoof(cell, null);
		}
		foreach (IntVec3 cell in CellRect.WholeMap(map))
		{
			foreach (Thing thing in cell.GetThingList(map).ToList())
			{
				if (thing is not Pawn)
				{
					thing.Destroy();
				}
			}
		}
	}
}
