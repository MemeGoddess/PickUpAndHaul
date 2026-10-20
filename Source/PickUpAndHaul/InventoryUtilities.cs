using System.Runtime.CompilerServices;

namespace PickUpAndHaul;

public static class InventoryUtilities
{
	extension(Thing thing)
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsNotCorpseOrAllowed() => Settings.AllowCorpses || thing is not Corpse;
	}

	extension(Pawn pawn)
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CompHauledToInventory HauledToInventoryComp()
		{
			return pawn?.TryGetComp<CompHauledToInventory>();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool HasHauledThingInInventory(Thing thing)
		{
			return pawn?.TryGetComp<CompHauledToInventory>()?.Contains(thing) ?? false;
		}
	}
}
