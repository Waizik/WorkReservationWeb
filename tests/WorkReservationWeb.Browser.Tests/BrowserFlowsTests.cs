using Microsoft.Playwright;

namespace WorkReservationWeb.Browser.Tests;

[CollectionDefinition(nameof(BrowserTestCollection), DisableParallelization = true)]
public sealed class BrowserTestCollection : ICollectionFixture<LocalAppHostFixture>;

[Collection(nameof(BrowserTestCollection))]
public sealed class BrowserFlowsTests(LocalAppHostFixture hostFixture) : IAsyncLifetime
{
    private IPlaywright? playwright;
    private IBrowser? browser;

    public async Task InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        playwright?.Dispose();
    }

    [Fact]
    public async Task BookingFlow_CreatesReservation_AndShowsItInAdmin()
    {
        var reservationEmail = $"browser-{Guid.NewGuid():N}@example.com";
        await using var context = await browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = hostFixture.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 1440, Height = 1200 }
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForSelectorAsync("#service");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#service option').length > 0");
        await page.WaitForSelectorAsync("#slot");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#slot option').length > 0");

        await page.FillAsync("#customerName", "Browser Test User");
        await page.FillAsync("#customerEmail", reservationEmail);
        await page.FillAsync("#note", "Created by Playwright browser coverage.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Book now" }).ClickAsync();

        await page.WaitForSelectorAsync("text=Reservation created.");

        await page.GotoAsync("/admin");
        await page.WaitForSelectorAsync("h1:has-text('Admin')");
        await page.WaitForSelectorAsync($"text={reservationEmail}");
    }

    [Fact]
    public async Task AdminFlow_CanCreateAndDeleteServiceOffer()
    {
        var title = $"Playwright Offer {Guid.NewGuid():N}";
        await using var context = await browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = hostFixture.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 1440, Height = 1200 }
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForSelectorAsync("a[href='admin']");
        await page.ClickAsync("a[href='admin']");

        await page.WaitForSelectorAsync("h1:has-text('Admin')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForSelectorAsync("#existingServiceOffer");
        await page.FillAsync("#serviceTitle", title);
        await page.FillAsync("#serviceDescription", "Service offer managed by browser automation.");
        await page.FillAsync("#serviceBasePrice", "75.50");
        await page.FillAsync("#serviceImageUrls", "https://example.invalid/browser-test.jpg");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save service offer" }).ClickAsync();

        await page.WaitForSelectorAsync("text=Service offer saved.", new() { Timeout = 60000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).First.ClickAsync();
        await page.WaitForSelectorAsync("text=Service offer deleted.");
        await page.WaitForFunctionAsync(
            "title => !Array.from(document.querySelectorAll('#existingServiceOffer option')).some(option => option.textContent?.includes(title))",
            title);
    }
}