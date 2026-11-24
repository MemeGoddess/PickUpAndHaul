using System.Linq;

namespace PickUpAndHaul;
public static class ModCompatibilityCheck
{
	public static bool NiceInventoryIsActive { get; } = ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Nice Inventory Tab");
	public static bool CombatExtendedIsActive { get; } = ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Combat Extended");

	public static bool AllowToolIsActive { get; } = ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Allow Tool");

	public static bool ExtendedStorageIsActive { get; } = ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "ExtendedStorageFluffyHarmonised")
		|| ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Extended Storage")
		|| ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Core SK");

	public static bool HCSKIsActive { get; } = ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == "Core SK");
}
