namespace MobileJourneys;

/// <summary>The mobile platform a test fixture runs on.</summary>
public enum TestPlatform
{
	/// <summary>Apple iOS simulator (XCUITest).</summary>
	iOS = 0,

	/// <summary>Google Android emulator (UiAutomator2).</summary>
	Android = 1,
}
