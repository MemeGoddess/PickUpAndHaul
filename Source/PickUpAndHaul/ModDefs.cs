using System.Diagnostics.CodeAnalysis;

namespace PickUpAndHaul;

[DefOf]
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Has to match defName")]
[SuppressMessage("ReSharper", "UnassignedField.Global", Justification = "Auto-populated by rimworld")]
public static class JobDefOfs
{
	static JobDefOfs() => DefOfHelper.EnsureInitializedInCtor(typeof(JobDefOfs));

	public static JobDef UnloadYourHauledInventory;
	public static JobDef HaulToInventory;
}

// This attribute doesn't really do conditional locating well
//[DefOf]
public static class DesignationDefs
{
	public static DesignationDef HaulUrgentlyDesignation { get; } = DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
}

[DefOf]
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Has to match defName")]
[SuppressMessage("ReSharper", "UnassignedField.Global", Justification = "Auto-populated by rimworld")]
public static class WorkGiverDefs
{
	static WorkGiverDefs() => DefOfHelper.EnsureInitializedInCtor(typeof(WorkGiverDefs));

	public static WorkGiverDef HaulToInventory;

	public static WorkGiver_Scanner HaulToInventoryWorker
	{
		get
		{
			// use a hard-cast and not 'as' to give a type error instead of a null pointer deref later
			field ??= (WorkGiver_Scanner)HaulToInventory.Worker;
			return field;
		}
	}
}
