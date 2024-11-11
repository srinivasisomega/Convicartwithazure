using ConvicartWebApp.BussinessLogicLayer.Interface;
using ConvicartWebApp.BussinessLogicLayer.Services;
using ConvicartWebApp.DataAccessLayer.Data;
using ConvicartWebApp.DataAccessLayer.Models;
using ConvicartWebApp.Filter;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace ConvicartWebApp.PresentationLayer.Controllers
{
    [TypeFilter(typeof(CustomerInfoFilter))]

    public class HomeController : Controller
    {
        private readonly ConvicartWarehouseContext _context;
        private readonly IPreferenceService PreferenceService;
        private readonly IStoreService StoreService;
        // Combined constructor for both _context and _logger
        public HomeController(ConvicartWarehouseContext context, ILogger<HomeController> logger, IPreferenceService preferenceService, IStoreService storeService)
        {
            _context = context;
            PreferenceService = preferenceService;
            StoreService = storeService;
            
        }
        public IActionResult Index()
        {
            return View();
        }
        public async Task<IActionResult> Indexafterlogin()
        {
            // Check if the customer is logged in
            if (HttpContext.Session.GetInt32("CustomerId") == null)
            {
                // If no customer session, redirect or return empty result
                return RedirectToAction("SignUp", "Customer");  // You can modify this as per your login logic
            }

            // Get CustomerId from session
            int customerId = HttpContext.Session.GetInt32("CustomerId").Value;

            // Fetch customer preferences using the PreferenceService
            var customerPreferences = await PreferenceService.GetCustomerPreferencesAsync(customerId);

            // Fetch products that match any of the customer's preferences using the StoreService
            var products = await StoreService.GetProductsByPreferencesAsync(customerPreferences);

            // Pass the products to the view
            return View(products);
        }


        public IActionResult Recipe()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        public IActionResult About()
        {
            return View();
        }
        // Action to handle the submission of the Query form
        [HttpPost]
        public async Task<IActionResult> Create([Bind("Id,Name,Mobile,Email,Description")] QuerySubmission querySubmission)
        {
            if (ModelState.IsValid)
            {
                _context.Add(querySubmission);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(querySubmission);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

