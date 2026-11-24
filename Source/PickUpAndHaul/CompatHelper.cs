using HarmonyLib;
using System.Diagnostics.CodeAnalysis;

namespace PickUpAndHaul;

[SuppressMessage("ReSharper", "InvertIf")]
internal class CompatHelper
{
	public static bool CeOverweight(Pawn pawn)
	{
		return false;
		//var ceCompInventory = pawn.GetComp<CombatExtended.CompInventory>();
		//return (ceCompInventory.currentWeight / ceCompInventory.capacityWeight) >= Settings.MaximumOccupiedCapacityToConsiderHauling;
	}

	public static int CanFitInInventory(Pawn pawn, Thing thing)
	{
		return thing.stackCount;
		//pawn.GetComp<CombatExtended.CompInventory>().CanFitInInventory(thing, out var countToPickUp);
		//return countToPickUp;
	}

	public static void UpdateInventory(Pawn pawn)
	{
		if (ModCompatibilityCheck.CombatExtendedIsActive)
		{
			//pawn.GetComp<CombatExtended.CompInventory>().UpdateInventory();
		}

		if (ModCompatibilityCheck.NiceInventoryIsActive)
		{
			RecacheInventoryForNiceInventoryTab ??= Traverse.CreateWithType("NiceInventoryTab.ITab_Pawn_Gear_Patch").Field("shouldRecache");
			if (RecacheInventoryForNiceInventoryTab.FieldExists())
			{
				RecacheInventoryForNiceInventoryTab.SetValue(true);
			}
		}
	}

	public static Traverse RecacheInventoryForNiceInventoryTab;
}
