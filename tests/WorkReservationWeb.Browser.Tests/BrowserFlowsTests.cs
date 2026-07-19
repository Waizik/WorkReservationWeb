using Microsoft.Playwright;

namespace WorkReservationWeb.Browser.Tests;

[CollectionDefinition(nameof(BrowserTestCollection), DisableParallelization = true)]
public sealed class BrowserTestCollection : ICollectionFixture<LocalAppHostFixture>;

[Collection(nameof(BrowserTestCollection))]
[Trait("Category", "E2E")]
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
        await page.WaitForSelectorAsync(".service-card");
        await page.ClickAsync(".service-card");
        await page.WaitForSelectorAsync(".slot-chip");
        await page.ClickAsync(".slot-chip");

        await page.FillAsync("#customerName", "Browser Test User");
        await page.FillAsync("#customerEmail", reservationEmail);
        await page.FillAsync("#note", "Created by Playwright browser coverage.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Book now" }).ClickAsync();

        await page.WaitForSelectorAsync("text=Reservation created.");

        await page.GotoAsync("/admin");
        await page.WaitForSelectorAsync("h1:has-text('Admin')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).Last.ClickAsync();
        await page.WaitForSelectorAsync($"text={reservationEmail}", new() { Timeout = 60000 });
    }

    [Fact]
    public async Task AdminFlow_ImageUpload_OpensCropperAndAddsCroppedImage()
    {
        await using var context = await browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = hostFixture.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 1440, Height = 1200 }
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/admin");
        await page.WaitForSelectorAsync("h1:has-text('Admin')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForSelectorAsync("#serviceImageUpload");

        // Any real PNG works as the upload payload; a page screenshot avoids bundling a fixture file.
        var pngBytes = await page.ScreenshotAsync();
        await page.SetInputFilesAsync("#serviceImageUpload", new[]
        {
            new FilePayload { Name = "cropper-test.png", MimeType = "image/png", Buffer = pngBytes },
            new FilePayload { Name = "cropper-test-2.png", MimeType = "image/png", Buffer = pngBytes }
        });

        // Each selected file opens its own cropper modal in sequence.
        await page.WaitForSelectorAsync(".wr-modal");
        await page.GetByRole(AriaRole.Button, new() { Name = "Use cropped image" }).ClickAsync();
        await page.WaitForSelectorAsync(".wr-modal");
        await page.GetByRole(AriaRole.Button, new() { Name = "Use cropped image" }).ClickAsync();

        await page.WaitForSelectorAsync("text=Uploaded 'cropper-test.jpg', 'cropper-test-2.jpg'", new() { Timeout = 60000 });
        Assert.Equal(2, await page.Locator("img[alt='Service offer image']").CountAsync());
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
        await page.GetByRole(AriaRole.Button, new() { Name = "Save service offer" }).ClickAsync();

        await page.WaitForSelectorAsync("text=Service offer saved.", new() { Timeout = 60000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).First.ClickAsync();
        await page.WaitForSelectorAsync("text=Service offer deleted.");
        await page.WaitForFunctionAsync(
            "title => !Array.from(document.querySelectorAll('#existingServiceOffer option')).some(option => option.textContent?.includes(title))",
            title);
    }
}