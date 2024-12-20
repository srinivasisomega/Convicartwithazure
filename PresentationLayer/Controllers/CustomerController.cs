﻿using Microsoft.AspNetCore.Mvc;
using ConvicartWebApp.Filter;
using ConvicartWebApp.DataAccessLayer.Data;
using ConvicartWebApp.DataAccessLayer.Models;
using ConvicartWebApp.BussinessLogicLayer.Interface;
using ConvicartWebApp.PresentationLayer.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using System.Security.Claims;
using System.Net.Http;

using Microsoft.EntityFrameworkCore;
using ConvicartWebApp.BussinessLogicLayer.Services;
using System.ComponentModel.Design;
using System.IO;
using Azure.Storage.Blobs;
namespace ConvicartWebApp.PresentationLayer.Controllers
{
    /// <summary>
    /// This controller is responsible for signup, signin, supscription selection, preference selection, viewing profile page, updating profilepage,
    /// uploading profile photo, displaying profile photo, logout, forgot password
    /// </summary>
    [TypeFilter(typeof(CustomerInfoFilter))]
    public class CustomerController : Controller
    {
        private readonly ICustomerService CustomerService;
        private readonly IPreferenceService PreferenceService;
        private readonly IPasswordResetService PasswordResetService;
        private readonly ISubscriptionService SubscriptionService;
        public CustomerController(IConfiguration configuration,ConvicartWarehouseContext context, ISubscriptionService subscriptionService, ICustomerService customerService, IPreferenceService preferenceService, IPasswordResetService passwordResetService)
        {
            CustomerService = customerService;
            PreferenceService = preferenceService;
            PasswordResetService = passwordResetService;
            SubscriptionService = subscriptionService;
            _connectionString = configuration["AzureBlobStorage:ConnectionString"];
            _containerName = configuration["AzureBlobStorage:ContainerName"];
        }

        // POST: Handle SignIn by checking if there are matching email and password in the customer table, if
        //they match customer id is saved in session
        [HttpPost]
        public async Task<IActionResult> SignIn(string email, string password)
        {
            // Check if the model state is valid
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ViewData["SignInErrors"] = new List<string> { "Email and password are required." };
                return View("SignUp");
            }

            // Find the customer by email and password
            var customer = await CustomerService.GetCustomerByEmailAndPasswordAsync(email, password);

            if (customer == null)
            {
                ViewData["SignInErrors"] = new List<string> { "Invalid email or password." };
                return View("SignUp");
            }

            // Store CustomerId in session
            HttpContext.Session.SetInt32("CustomerId", customer.CustomerId);

            return RedirectToAction("Profile", new { customerId = customer.CustomerId });
        }

        // GET: Renders the Signup page and sigin partial views
        public IActionResult SignUp()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SignUp([Bind("Name,Number,Email,Password")] Customer customer)
        {
            if (ModelState.IsValid)
            {
                // Check if email already exists
                var existingCustomer = await CustomerService.GetCustomerByEmailAsync(customer.Email);


                if (existingCustomer != null)
                {
                    ModelState.AddModelError("Email", "Email is already in use.");
                    return View(customer); // Return the view with an error message
                }

                await CustomerService.AddCustomerAsync(customer);

                // Store CustomerId in session
                HttpContext.Session.SetInt32("CustomerId", customer.CustomerId);

                return RedirectToAction("Subscription");
            }

            return View(customer);
        }
        [HttpGet]
        public IActionResult SignInWithGoogle()
        {
            var properties = new AuthenticationProperties { RedirectUri = Url.Action("GoogleResponse") };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public async Task<IActionResult> GoogleResponse()
        {
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!result.Succeeded) return RedirectToAction("SignUp");

            var email = result.Principal.FindFirstValue(ClaimTypes.Email);
            var name = result.Principal.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrEmpty(email))
            {
                ViewData["SignInErrors"] = new List<string> { "Google login failed. Please try again." };
                return RedirectToAction("SignUp");
            }

            // Check if user already exists using LINQ
            var customer = await CustomerService.GetCustomerByEmailAsync(email);

            if (customer == null)
            {
                // New user, create a Customer entry
                customer = new Customer
                {
                    Name = name,
                    Number = "Change number",
                    Email = email,
                    Password = "Change Password",
                    PointBalance = 0,
                };

                await CustomerService.AddCustomerAsync(customer);

            }

            // Store CustomerId in session
            HttpContext.Session.SetInt32("CustomerId", customer.CustomerId);

            return RedirectToAction("Profile", new { customerId = customer.CustomerId });
        }



        // GET: checks if there is customer id in session if yes sends the customer's data to view for selection the type of subscription.
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> Subscription(int customerId)
        {
            var customer = CustomerService.GetCustomerByIdAsync(customerId);
            if (customer == null) return NotFound();

            return View(customer);
        }

        //this method is responsible for add no of days to subscription date, adding points to point balence, and adding subscription to the customers table.
        [HttpPost]
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> UpdateSubscription(string subscriptionType, int days, decimal amount, int customerId)
        {
            // Attempt to update the subscription
            if (!SubscriptionService.UpdateSubscription(subscriptionType, days, amount, customerId, out string errorMessage))
            {
                // If update fails, add error to ModelState and redirect to Subscription view
                ModelState.AddModelError("", errorMessage);
                return RedirectToAction("Subscription", new { customerId = customerId });
            }

            // Asynchronously check if customer has preferences
            bool hasPreferences = await PreferenceService.CustomerHasPreferencesAsync(customerId);

            // Redirect based on the existence of preferences
            if (hasPreferences)
            {
                return RedirectToAction("Profile");
            }
            else
            {
                return RedirectToAction("Preferences");
            }
        }


        // GET: Preferences Page  checks if there is customer id in session if yes sends the customer's data to view for selection of preferences.
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> Preferences(int customerId)
        {

            var preferences = await PreferenceService.GetPreferencesAsync().ConfigureAwait(false);
            ViewBag.CustomerId = customerId;

            return View(preferences);
        }

        [HttpPost]
        [SessionAuthorize]
        public async Task<IActionResult> SavePreferences(List<int> selectedPreferences)
        {
            int? customerId = HttpContext.Session.GetInt32("CustomerId");

            await PreferenceService.UpdateCustomerPreferencesAsync(customerId.Value, selectedPreferences).ConfigureAwait(false);

            return RedirectToAction("Profile", new { customerId });
        }

        // GET: Profile Page this page displays the address, preferences, pointbalence, subscription and other importent details related to customer
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> Profile(int? customerId)
        {
            if (!customerId.HasValue) return BadRequest();

            var customer = await CustomerService.GetCustomerWithAddressByIdAsync(customerId.Value);
            if (customer == null) return NotFound();

            var customerPreferences = await PreferenceService.GetCustomerPreferencesListAsync(customerId.Value);

            var viewModel = new CustomerProfileViewModel
            {
                Customer = customer,
                Address = customer.Address,
                CustomerPreferences = customerPreferences
            };

            return View(viewModel);
        }

        // it is responsible for rendering a view that show existing preferences slected by customer and show all other preferences from preferences table.
        [SessionAuthorize]
        public async Task<IActionResult> UpdatePreference()
        {
            int? customerId = HttpContext.Session.GetInt32("CustomerId");
            if (customerId == null)
            {
                return RedirectToAction("Subscription");
            }

            var preferences = await PreferenceService.GetPreferencesAsync().ConfigureAwait(false);
            var selectedPreferences = await PreferenceService.GetCustomerPreferencesAsync(customerId.Value).ConfigureAwait(false);

            ViewBag.CustomerId = customerId;
            ViewBag.SelectedPreferences = selectedPreferences;

            return View(preferences);
        }

        [HttpPost]
        [SessionAuthorize]
        public async Task<IActionResult> UpdatedPreferences(List<int> selectedPreferences)
        {
            int? customerId = HttpContext.Session.GetInt32("CustomerId");
            await PreferenceService.UpdateCustomerPreferencesAsync(customerId.Value, selectedPreferences).ConfigureAwait(false);

            return RedirectToAction("Profile", new { customerId });
        }

        [HttpGet]
        [SessionAuthorize]
        public IActionResult EditProfile()
        {
            var customerId = HttpContext.Session.GetInt32("CustomerId");
            var customer = CustomerService.GetCustomerByIdAsync(customerId.Value).Result;
            if (customer == null) return NotFound();

            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfileSave(Customer model)
        {
            await CustomerService.UpdateCustomerProfileAsync(model);
            return RedirectToAction("Profile", new { customerId = model.CustomerId });
        }

        //renders a page to upload the image.
        [HttpGet]
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public IActionResult UploadProfileImage(int? customerId)
        {
            var customer = CustomerService.GetCustomerById(customerId);
            if (customer == null)
            {
                return NotFound();  // Handle case where customer is not found
            }

            return View(customer);  // Return the customer model to the view
        }

        [HttpPost]
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> UploadProfileImageSave(IFormFile image, int? customerId)
        {
            if (customerId == null)
            {
                return NotFound();  // Invalid customer ID
            }

            var isSaved = await CustomerService.SaveProfileImageAsync(image, customerId.Value);

            if (!isSaved)
            {
                return NotFound();  // Handle case where customer or image is invalid
            }

            return RedirectToAction("Profile", new { id = customerId });
        }
        [SessionAuthorize]
        public IActionResult GetProfileImage(int id)
        {
            var customer = CustomerService.GetCustomerById(id);
            if (customer?.ProfileImage != null)
            {
                return File(customer.ProfileImage, "image/jpeg");  // Serve image
            }
            else
            {
                return NotFound();  // No image found
            }
        }
        private static Dictionary<string, string> resetCodes = new Dictionary<string, string>();

        // GET: Customer/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // POST: Customer/ForgotPassword
        [HttpPost]
        public IActionResult ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ModelState.AddModelError("Email", "Email is required.");
                return View();
            }

            // Use the service to generate and send the reset code
            PasswordResetService.GenerateAndSendResetCode(email);

            TempData["Message"] = "If the email exists, a reset code has been sent.";
            TempData["Email"] = email; // Store email in TempData
            return RedirectToAction("ForgotPasswordConfirmation");
        }

        // GET: Customer/ForgotPasswordConfirmation
        public IActionResult ForgotPasswordConfirmation()
        {
            // Re-store and keep TempData for the email
            TempData.Keep("Email");
            ViewBag.Email = TempData["Email"]; // Display email for verification if needed
            return View();
        }

        // GET: Customer/ResetPassword
        [HttpGet]
        public IActionResult ResetPassword()
        {
            // Retrieve email from TempData
            var email = TempData["Email"] as string;

            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("ForgotPassword");
            }

            // Re-store email in TempData and keep it for subsequent requests
            TempData["Email"] = email;
            TempData.Keep("Email");

            return View(new ResetPasswordModel { Email = email });
        }
        // POST: Customersword
        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Get the customer by email using the async method and await the result
            var customer = await CustomerService.GetCustomerByEmailAsync(model.Email);
            if (customer == null)
            {
                ModelState.AddModelError("", "Customer not found.");
                return View(model);
            }

            // Change the customer's password asynchronously and await the call
            await CustomerService.ChangePasswordAsync(customer, model.NewPassword);

            TempData["Message"] = "Your password has been successfully reset.";
            return RedirectToAction("ResetPasswordConfirmation");
        }

        // GET: Customer/ResetPasswordConfirmation
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }


        [HttpGet]
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public IActionResult ChangePassword(int? customerId)
        {

            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, int? customerId)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Use the service to retrieve the customer
            var customer = CustomerService.GetCustomerById(customerId);
            if (customer == null)
            {
                return NotFound();
            }

            // Verify the current password using the service
            if (!CustomerService.VerifyPassword(customer, model.CurrentPassword))
            {
                ModelState.AddModelError("", "Current password is incorrect.");
                return View(model);
            }

            // Update the password using the service
            await CustomerService.ChangePasswordAsync(customer, model.NewPassword);

            TempData["SuccessMessage"] = "Password changed successfully.";
            return RedirectToAction("Profile");
        }
        public IActionResult Logout()
        {
            // Clear the CustomerId from the session
            HttpContext.Session.Remove("CustomerId");
            return RedirectToAction("Index", "Home");
        }
        [ServiceFilter(typeof(CustomerAuthorizationFilter))]
        [SessionAuthorize]
        public async Task<IActionResult> GetSubscription(int customerId)
        {
            var viewModel = await SubscriptionService.GetCustomerSubscriptionAsync(customerId);

            if (viewModel == null)
                return NotFound();

            return View("SubscriptionUpdate", viewModel);
        }
        private readonly string _connectionString;
        private readonly string _containerName;
        


        [HttpPost]
        [Route("Customer/UploadChunk")]
        public async Task<IActionResult> UploadChunk(IFormFile file, string fileName, int chunkIndex, int totalChunks, int customerId)
        {
            // Retrieve customer details
            var customer = CustomerService.GetCustomerById(customerId);
            if (customer == null || string.IsNullOrWhiteSpace(customer.Name))
            {
                return BadRequest("Invalid customer.");
            }

            // Sanitize customer name and file name
            var sanitizedCustomerName = SanitizeBlobName(customer.Name);
            var sanitizedFileName = SanitizeBlobName(fileName);

            // Construct the blob name and path
            var blobFolderPath = $"{sanitizedCustomerName}/";
            var blobChunkName = $"{blobFolderPath}{sanitizedFileName}.part_{chunkIndex}";

            try
            {
                // Upload the file chunk to Azure Blob Storage
                var blobServiceClient = new BlobServiceClient(_connectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = blobContainerClient.GetBlobClient(blobChunkName);

                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // Combine chunks if this is the last one
                if (chunkIndex == totalChunks - 1)
                {
                    await CombineChunks(blobFolderPath, sanitizedFileName, totalChunks, blobContainerClient);
                }

                return Ok();
            }
            catch (Azure.RequestFailedException ex)
            {
                // Log the exception and return a user-friendly error message
                Console.Error.WriteLine($"Error uploading blob: {ex.Message}");
                return StatusCode(500, "Failed to upload file chunk. Please try again.");
            }
        }


        private async Task CombineChunks(string blobFolderPath, string sanitizedFileName, int totalChunks, BlobContainerClient blobContainerClient)
        {
            var finalBlobName = $"{blobFolderPath}{sanitizedFileName}";

            // Download and combine chunks
            using (var memoryStream = new MemoryStream())
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    var chunkBlobName = $"{blobFolderPath}{sanitizedFileName}.part_{i}";
                    var chunkBlobClient = blobContainerClient.GetBlobClient(chunkBlobName);

                    var downloadResponse = await chunkBlobClient.DownloadAsync();
                    await downloadResponse.Value.Content.CopyToAsync(memoryStream);

                    // Delete the chunk blob after appending
                    await chunkBlobClient.DeleteAsync();
                }

                memoryStream.Position = 0;
                var finalBlobClient = blobContainerClient.GetBlobClient(finalBlobName);
                await finalBlobClient.UploadAsync(memoryStream, overwrite: true);
            }
        }

        public async Task<IActionResult> ForYou(int page = 1)
        {
            var customerId = HttpContext.Items["CustomerId"] as int?;
            if (customerId == null) return RedirectToAction("SignUp", "Customer");

            var blobServiceClient = new BlobServiceClient(_connectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(_containerName);
            var customer = await CustomerService.GetCustomerByIdAsync(customerId.Value);
            if (customer == null) return RedirectToAction("SignUp", "Customer");

            var blobs = blobContainerClient.GetBlobsAsync(prefix: $"{customer.Name}/");
            var videoUrls = new List<string>();

            await foreach (var blobItem in blobs)
            {
                var blobUri = blobContainerClient.GetBlobClient(blobItem.Name).Uri.ToString();
                videoUrls.Add(blobUri);
            }

            var pageSize = 4;
            var totalVideos = videoUrls.Count;
            var totalPages = (int)Math.Ceiling((double)totalVideos / pageSize);

            var videosOnCurrentPage = videoUrls
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(url => new VideoPostViewModel
                {
                    VideoPath = url,
                    FileName = Path.GetFileName(url)
                })
                .ToList();

            var viewModel = new ForYouPageViewModel
            {
                Customer = customer,
                VideoPosts = videosOnCurrentPage,
                PageIndex = page,
                TotalPages = totalPages
            };

            return View(viewModel);
        }

        private string SanitizeBlobName(string name)
        {
            // Replace invalid characters with a hyphen
            var invalidChars = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', '?', '#', '[', ']' });
            foreach (var invalidChar in invalidChars)
            {
                name = name.Replace(invalidChar, '-');
            }

            // Trim and return sanitized name
            return name.Trim();
        }



    }

}


