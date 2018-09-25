using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.Email;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Models;
using OrchardCore.Modules;
using OrchardCore.SaaS.ViewModels;
using OrchardCore.Setup.Services;

namespace OrchardCore.SaaS.Controllers
{
    public class HomeController : Controller
    {
        private readonly IClock _clock;
        private readonly ISmtpService _smtpService;
        private readonly ISetupService _setupService;
        private readonly IShellSettingsManager _shellSettingsManager;


        public HomeController(
            IClock clock,
            ISmtpService smtpService,
            ISetupService setupService,
            IShellSettingsManager shellSettingsManager)
        {
            _clock = clock;
            _smtpService = smtpService;
            _setupService = setupService;
            _shellSettingsManager = shellSettingsManager;
        }


        public IActionResult Index(RegisterUserViewModel viewModel)
        {
            return View(viewModel);
        }

        [HttpPost, ActionName(nameof(Index))]
        public async Task<IActionResult> IndexPost(RegisterUserViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var shellSettings = new ShellSettings
                {
                    Name = viewModel.Handle,
                    RequestUrlPrefix = viewModel.Handle,
                    RequestUrlHost = "",
                    // This should be a setting in the SaaS module.
                    ConnectionString = "",
                    TablePrefix = "",
                    DatabaseProvider = "Sqlite",
                    State = TenantState.Uninitialized,
                    Secret = Guid.NewGuid().ToString(),
                    RecipeName = "Blog"
                };

                _shellSettingsManager.SaveSettings(shellSettings);

                var confirmationLink = Url.Action(nameof(HomeController.Confirm), "Home", new { email = viewModel.Email, handle = viewModel.Handle, siteName = viewModel.SiteName }, Request.Scheme);

                var message = new MailMessage
                {
                    From = new MailAddress("admin@orchard.com", "Orchard SaaS"),
                    IsBodyHtml = true,
                    Body = $"Click <a href=\"{HttpUtility.HtmlEncode(confirmationLink)}\">this link</a>"
                };

                message.To.Add(viewModel.Email);

                await _smtpService.SendAsync(message);

                return RedirectToAction(nameof(Success));
            }

            return View(nameof(Index), viewModel);
        }

        public IActionResult Success()
        {
            return View();
        }

        public async Task<IActionResult> Confirm(string email, string handle, string siteName)
        {
            if (!_shellSettingsManager.TryGetSettings(handle, out var shellSettings))
                return NotFound();

            var recipes = await _setupService.GetSetupRecipesAsync();
            var recipe = recipes.FirstOrDefault(x => x.Name == shellSettings.RecipeName);

            if (recipe == null)
                return NotFound();

            var setupContext = new SetupContext
            {
                ShellSettings = shellSettings,
                SiteName = siteName,
                EnabledFeatures = null,
                AdminUsername = "admin",
                AdminEmail = email,
                AdminPassword = "Demo123!",
                Errors = new Dictionary<string, string>(),
                Recipe = recipe,
                SiteTimeZone = _clock.GetSystemTimeZone().TimeZoneId,
                DatabaseProvider = shellSettings.DatabaseProvider,
                DatabaseConnectionString = shellSettings.ConnectionString,
                DatabaseTablePrefix = shellSettings.TablePrefix
            };

            await _setupService.SetupAsync(setupContext);

            // Check if a component in the Setup failed.
            if (setupContext.Errors.Any())
            {
                foreach (var error in setupContext.Errors)
                {
                    ModelState.AddModelError(error.Key, error.Value);
                }

                return Redirect("Error");
            }

            return Redirect("~/" + handle);
        }
    }
}
