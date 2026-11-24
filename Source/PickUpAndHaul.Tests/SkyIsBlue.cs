using DevTools.Testing;

namespace PickUpAndHaul.Tests;

[TestFixture(TestType.Playing)]
public class SkyIsBlue
{
	[Test]
	public void LookUp()
	{
		Expect.IsTrue(true, "The sky was not blue, somehow");
	}
}
