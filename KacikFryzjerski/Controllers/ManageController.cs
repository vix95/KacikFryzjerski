﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using KacikFryzjerski.Models;
using System.Collections.Generic;
using System.Data.Entity;
using KacikFryzjerski.ViewModel;
using System.IO;
using KacikFryzjerski.Infrastructure;
using KacikFryzjerski.DAL;

namespace KacikFryzjerski.Controllers
{
    [Authorize]
    public class ManageController : Controller
    {
        private ApplicationSignInManager _signInManager;
        private ApplicationUserManager _userManager;
        private ProjectDbContext db;

        public ManageController()
        {
            this.db = new ProjectDbContext();
        }

        public ManageController(ProjectDbContext context)
        {
            this.db = context;
        }

        public ManageController(ApplicationUserManager userManager, ApplicationSignInManager signInManager)
        {
            UserManager = userManager;
            SignInManager = signInManager;
        }

        public ApplicationSignInManager SignInManager
        {
            get
            {
                return _signInManager ?? HttpContext.GetOwinContext().Get<ApplicationSignInManager>();
            }
            private set 
            { 
                _signInManager = value; 
            }
        }

        public ApplicationUserManager UserManager
        {
            get
            {
                return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>();
            }
            private set
            {
                _userManager = value;
            }
        }

        //
        // GET: /Manage/Index
        public async Task<ActionResult> Index(ManageMessageId? message)
        {
            ViewBag.StatusMessage =
                message == ManageMessageId.ChangePasswordSuccess ? "Zmieniono hasło."
                : message == ManageMessageId.SetPasswordSuccess ? "Ustawiono hasło."
                : message == ManageMessageId.SetTwoFactorSuccess ? "Ustawiono dostawcę uwierzytelniania dwuetapowego."
                : message == ManageMessageId.Error ? "Wystąpił błąd."
                : message == ManageMessageId.AddPhoneSuccess ? "Dodano numer telefonu."
                : message == ManageMessageId.RemovePhoneSuccess ? "Usunięto numer telefonu."
                : "";

            var userId = User.Identity.GetUserId();
            var model = new IndexViewModel
            {
                HasPassword = HasPassword(),
                PhoneNumber = await UserManager.GetPhoneNumberAsync(userId),
                TwoFactor = await UserManager.GetTwoFactorEnabledAsync(userId),
                Logins = await UserManager.GetLoginsAsync(userId),
                BrowserRemembered = await AuthenticationManager.TwoFactorBrowserRememberedAsync(userId)
            };
            return View(model);
        }

        //
        // POST: /Manage/RemoveLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RemoveLogin(string loginProvider, string providerKey)
        {
            ManageMessageId? message;
            var result = await UserManager.RemoveLoginAsync(User.Identity.GetUserId(), new UserLoginInfo(loginProvider, providerKey));
            if (result.Succeeded)
            {
                var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
                if (user != null)
                {
                    await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                }
                message = ManageMessageId.RemoveLoginSuccess;
            }
            else
            {
                message = ManageMessageId.Error;
            }
            return RedirectToAction("ManageLogins", new { Message = message });
        }

        //
        // GET: /Manage/AddPhoneNumber
        public ActionResult AddPhoneNumber()
        {
            return View();
        }

        //
        // POST: /Manage/AddPhoneNumber
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> AddPhoneNumber(AddPhoneNumberViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            // Wygeneruj i wyślij token
            var code = await UserManager.GenerateChangePhoneNumberTokenAsync(User.Identity.GetUserId(), model.Number);
            if (UserManager.SmsService != null)
            {
                var message = new IdentityMessage
                {
                    Destination = model.Number,
                    Body = "Twój kod zabezpieczający: " + code
                };
                await UserManager.SmsService.SendAsync(message);
            }
            return RedirectToAction("VerifyPhoneNumber", new { PhoneNumber = model.Number });
        }

        //
        // POST: /Manage/EnableTwoFactorAuthentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EnableTwoFactorAuthentication()
        {
            await UserManager.SetTwoFactorEnabledAsync(User.Identity.GetUserId(), true);
            var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
            if (user != null)
            {
                await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
            }
            return RedirectToAction("Index", "Manage");
        }

        //
        // POST: /Manage/DisableTwoFactorAuthentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DisableTwoFactorAuthentication()
        {
            await UserManager.SetTwoFactorEnabledAsync(User.Identity.GetUserId(), false);
            var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
            if (user != null)
            {
                await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
            }
            return RedirectToAction("Index", "Manage");
        }

        //
        // GET: /Manage/VerifyPhoneNumber
        public async Task<ActionResult> VerifyPhoneNumber(string phoneNumber)
        {
            var code = await UserManager.GenerateChangePhoneNumberTokenAsync(User.Identity.GetUserId(), phoneNumber);
            // Wyślij wiadomość SMS za pośrednictwem dostawcy usług SMS w celu zweryfikowania numeru telefonu
            return phoneNumber == null ? View("Error") : View(new VerifyPhoneNumberViewModel { PhoneNumber = phoneNumber });
        }

        //
        // POST: /Manage/VerifyPhoneNumber
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> VerifyPhoneNumber(VerifyPhoneNumberViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var result = await UserManager.ChangePhoneNumberAsync(User.Identity.GetUserId(), model.PhoneNumber, model.Code);
            if (result.Succeeded)
            {
                var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
                if (user != null)
                {
                    await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                }
                return RedirectToAction("Index", new { Message = ManageMessageId.AddPhoneSuccess });
            }
            // Jeśli dotarliśmy tak daleko, oznacza to, że wystąpił błąd. Wyświetl ponownie formularz
            ModelState.AddModelError("", "Nie można zweryfikować numeru telefonu");
            return View(model);
        }

        //
        // POST: /Manage/RemovePhoneNumber
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RemovePhoneNumber()
        {
            var result = await UserManager.SetPhoneNumberAsync(User.Identity.GetUserId(), null);
            if (!result.Succeeded)
            {
                return RedirectToAction("Index", new { Message = ManageMessageId.Error });
            }
            var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
            if (user != null)
            {
                await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
            }
            return RedirectToAction("Index", new { Message = ManageMessageId.RemovePhoneSuccess });
        }

        //
        // GET: /Manage/ChangePassword
        public ActionResult ChangePassword()
        {
            return View();
        }

        //
        // POST: /Manage/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var result = await UserManager.ChangePasswordAsync(User.Identity.GetUserId(), model.OldPassword, model.NewPassword);
            if (result.Succeeded)
            {
                var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
                if (user != null)
                {
                    await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                }
                return RedirectToAction("Index", new { Message = ManageMessageId.ChangePasswordSuccess });
            }
            AddErrors(result);
            return View(model);
        }

        //
        // GET: /Manage/SetPassword
        public ActionResult SetPassword()
        {
            return View();
        }

        //
        // POST: /Manage/SetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await UserManager.AddPasswordAsync(User.Identity.GetUserId(), model.NewPassword);
                if (result.Succeeded)
                {
                    var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
                    if (user != null)
                    {
                        await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                    }
                    return RedirectToAction("Index", new { Message = ManageMessageId.SetPasswordSuccess });
                }
                AddErrors(result);
            }

            // Jeśli dotarliśmy tak daleko, oznacza to, że wystąpił błąd. Wyświetl ponownie formularz
            return View(model);
        }

        //
        // GET: /Manage/ManageLogins
        public async Task<ActionResult> ManageLogins(ManageMessageId? message)
        {
            ViewBag.StatusMessage =
                message == ManageMessageId.RemoveLoginSuccess ? "Usunięto logowanie zewnętrzne."
                : message == ManageMessageId.Error ? "Wystąpił błąd."
                : "";
            var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
            if (user == null)
            {
                return View("Error");
            }
            var userLogins = await UserManager.GetLoginsAsync(User.Identity.GetUserId());
            var otherLogins = AuthenticationManager.GetExternalAuthenticationTypes().Where(auth => userLogins.All(ul => auth.AuthenticationType != ul.LoginProvider)).ToList();
            ViewBag.ShowRemoveButton = user.PasswordHash != null || userLogins.Count > 1;
            return View(new ManageLoginsViewModel
            {
                CurrentLogins = userLogins,
                OtherLogins = otherLogins
            });
        }

        //
        // POST: /Manage/LinkLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LinkLogin(string provider)
        {
            // Żądaj przekierowania do dostawcy logowania zewnętrznego w celu połączenia logowania bieżącego użytkownika
            return new AccountController.ChallengeResult(provider, Url.Action("LinkLoginCallback", "Manage"), User.Identity.GetUserId());
        }

        //
        // GET: /Manage/LinkLoginCallback
        public async Task<ActionResult> LinkLoginCallback()
        {
            var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync(XsrfKey, User.Identity.GetUserId());
            if (loginInfo == null)
            {
                return RedirectToAction("ManageLogins", new { Message = ManageMessageId.Error });
            }
            var result = await UserManager.AddLoginAsync(User.Identity.GetUserId(), loginInfo.Login);
            return result.Succeeded ? RedirectToAction("ManageLogins") : RedirectToAction("ManageLogins", new { Message = ManageMessageId.Error });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _userManager != null)
            {
                _userManager.Dispose();
                _userManager = null;
            }

            base.Dispose(disposing);
        }

        public ActionResult OrdersList()
        {
            bool is_admin = User.IsInRole("Admin"); // role
            ViewBag.UserIsAdmin = is_admin;
            IEnumerable<OrderModels> orderList;

            // show all for administrator
            if (is_admin)
            {
                orderList = db.Orders.Include("OrderPosition").OrderByDescending(x => x.Order_ordered_at).ToList();
                foreach (OrderModels order in orderList)
                {
                    foreach (OrderPositionModels orderPosition in order.OrderPosition)
                    {
                        orderPosition.OrderPosition_ProductElement = db.Products.Find(orderPosition.OrderPosition_product_id);
                    }
                }
            }
            else
            {
                var user_id = User.Identity.GetUserId();
                orderList = db.Orders.Where(x => x.Order_User_id == user_id).Include("OrderPosition")
                    .OrderByDescending(x => x.Order_ordered_at).ToList();

                foreach (OrderModels order in orderList)
                {
                    foreach (OrderPositionModels orderPosition in order.OrderPosition)
                    {
                        orderPosition.OrderPosition_ProductElement = db.Products.Find(orderPosition.OrderPosition_product_id);
                    }
                }
            }

            return View(orderList);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public OrderStatus ChangeOrderStatus(OrderModels order)
        {
            OrderModels order_to_modification = db.Orders.Find(order.Id);
            order_to_modification.Order_order_status = order.Order_order_status;
            db.SaveChanges();

            return order.Order_order_status;
        }

        [Authorize(Roles = "Admin")]
        public ActionResult AddProduct(int? product_id, bool? confirmation)
        {
            ProductModels product;
            if (product_id.HasValue)
            {
                ViewBag.EditMode = true;
                product = db.Products.Find(product_id);
            }
            else
            {
                ViewBag.EditMode = false;
                product = new ProductModels();
            }

            var result = new EditProductViewModel();
            result.Categories = db.Categories.ToList();
            result.Product = product;
            result.Confirmation = confirmation;

            return View(result);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public ActionResult AddProduct(EditProductViewModel model, HttpPostedFileBase file)
        {
            ViewBag.EditMode = false;
            if (model.Product.Id > 0)
            {
                db.Entry(model.Product).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("AddProduct", new { confirmation = true });
            }
            else
            {
                // if files has not been uploaded
                if (file != null && file.ContentLength > 0)
                {
                    if (ModelState.IsValid)
                    {
                        var fileExt = Path.GetExtension(file.FileName);
                        var filename = Guid.NewGuid() + fileExt;

                        var path = Path.Combine(Server.MapPath(AppConfig.StoreImagesCatalog), filename);
                        file.SaveAs(path);

                        model.Product.Product_image_path = filename;
                        model.Product.Product_created_at = DateTime.Now;

                        db.Entry(model.Product).State = EntityState.Added;
                        db.SaveChanges();

                        return RedirectToAction("AddProduct", new { confirmation = true });
                    }
                    else
                    {
                        var categories = db.Categories.ToList();
                        model.Categories = categories;
                        return View(model);
                    }
                }
                else
                {
                    ModelState.AddModelError("", "Nie wskazano pliku");
                    var kategorie = db.Categories.ToList();
                    model.Categories = kategorie;
                    return View(model);
                }
            }

        }

        [Authorize(Roles = "Admin")]
        public ActionResult DeleteProduct(int product_id)
        {
            var product = db.Products.Find(product_id);
            db.Products.Remove(product);
            db.SaveChanges();

            return RedirectToAction("AddProduct", new { confirmation = true });
        }

        #region Pomocnicy
        // Służy do ochrony XSRF podczas dodawania logowań zewnętrznych
        private const string XsrfKey = "XsrfId";

        private IAuthenticationManager AuthenticationManager
        {
            get
            {
                return HttpContext.GetOwinContext().Authentication;
            }
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error);
            }
        }

        private bool HasPassword()
        {
            var user = UserManager.FindById(User.Identity.GetUserId());
            if (user != null)
            {
                return user.PasswordHash != null;
            }
            return false;
        }

        private bool HasPhoneNumber()
        {
            var user = UserManager.FindById(User.Identity.GetUserId());
            if (user != null)
            {
                return user.PhoneNumber != null;
            }
            return false;
        }

        public enum ManageMessageId
        {
            AddPhoneSuccess,
            ChangePasswordSuccess,
            SetTwoFactorSuccess,
            SetPasswordSuccess,
            RemoveLoginSuccess,
            RemovePhoneSuccess,
            Error
        }

#endregion
    }
}