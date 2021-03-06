﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using PopForums.Configuration;
using PopForums.Email;
using PopForums.Extensions;
using PopForums.Models;
using PopForums.Repositories;
using PopForums.Web;

namespace PopForums.Services
{
	public class UserService : IUserService
	{
		private readonly IUserRepository _userRepository;
		private readonly IRoleRepository _roleRepository;
		private readonly IProfileRepository _profileRepository;
		private readonly IFormsAuthenticationWrapper _formsAuthWrapper;
		private readonly ISettingsManager _settingsManager;
		private readonly IUserAvatarRepository _userAvatarRepository;
		private readonly IUserImageRepository _userImageRepository;
		private readonly ISecurityLogService _securityLogService;
		private readonly ITextParsingService _textParsingService;
		private readonly IBanRepository _banRepository;
		private readonly IForgotPasswordMailer _forgotPasswordMailer;
		private readonly IImageService _imageService;

		public UserService(IUserRepository userRepository, IRoleRepository roleRepository, IProfileRepository profileRepository, IFormsAuthenticationWrapper formsAuthWrapper, ISettingsManager settingsManager, IUserAvatarRepository userAvatarRepository, IUserImageRepository userImageRepository, ISecurityLogService securityLogService, ITextParsingService textParsingService, IBanRepository banRepository, IForgotPasswordMailer forgotPasswordMailer, IImageService imageService)
		{
			_userRepository = userRepository;
			_roleRepository = roleRepository;
			_profileRepository = profileRepository;
			_formsAuthWrapper = formsAuthWrapper;
			_settingsManager = settingsManager;
			_userAvatarRepository = userAvatarRepository;
			_userImageRepository = userImageRepository;
			_securityLogService = securityLogService;
			_textParsingService = textParsingService;
			_banRepository = banRepository;
			_forgotPasswordMailer = forgotPasswordMailer;
			_imageService = imageService;
		}

		public void SetPassword(User targetUser, string password, string ip, User user)
		{
			var salt = Guid.NewGuid();
			var hashedPassword = password.GetMD5Hash(salt);
			_userRepository.SetHashedPassword(targetUser, hashedPassword, salt);
			_securityLogService.CreateLogEntry(user, targetUser, ip, String.Empty, SecurityLogType.PasswordChange);
		}

		public bool CheckPassword(string email, string password, out Guid? salt)
		{
			string hashedPassword;
			var storedHash = _userRepository.GetHashedPasswordByEmail(email, out salt);
			if (salt.HasValue)
				hashedPassword = password.GetMD5Hash(salt.Value);
			else
				hashedPassword =  password.GetMD5Hash();
			return storedHash == hashedPassword;
		}

		public User GetUser(int userID)
		{
			var user = _userRepository.GetUser(userID);
			PopulateRoles(user);
			return user;
		}

		public User GetUserByName(string name)
		{
			var user = _userRepository.GetUserByName(name);
			PopulateRoles(user);
			return user;
		}

		public User GetUserByAuhtorizationKey(Guid authorizationKey)
		{
			var user = _userRepository.GetUserByAuthorizationKey(authorizationKey);
			PopulateRoles(user);
			return user;
		}

		public User GetUserByEmail(string email)
		{
			if (String.IsNullOrWhiteSpace(email))
				return null;
			var user = _userRepository.GetUserByEmail(email);
			PopulateRoles(user);
			return user;
		}

		public List<User> GetUsersFromIDs(IList<int> ids)
		{
			return _userRepository.GetUsersFromIDs(ids);
		}

		private void PopulateRoles(User user)
		{
			if (user != null)
				user.Roles = _roleRepository.GetUserRoles(user.UserID);
		}

		public bool IsNameInUse(string name)
		{
			return GetUserByName(name) != null;
		}

		public bool IsEmailInUse(string email)
		{
			return GetUserByEmail(email) != null;
		}

		public bool IsEmailInUseByDifferentUser(User user, string email)
		{
			var otherUser = GetUserByEmail(email);
			if (otherUser == null)
				return false;
			return otherUser.Email != user.Email;
		}

		public bool IsIPBanned(string ip)
		{
			return _banRepository.IPIsBanned(ip);
		}

		public bool IsEmailBanned(string email)
		{
			return _banRepository.EmailIsBanned(email);
		}

		public User CreateUser(SignupData signupData, string ip)
		{
			return CreateUser(signupData.Name, signupData.Email, signupData.Password, _settingsManager.Current.IsNewUserApproved, ip,signupData.UserType, signupData.DepartmentId);
		}
        public List<User> GetAll()
        {
            return _userRepository.GetAll();
        }

        public User CreateUser(string name, string email, string password, bool isApproved, string ip, int userType, int departmentId)
		{
			name = _textParsingService.Censor(name);
			if (!email.IsEmailAddress())
				throw new Exception("E-mail address invalid.");
			if (String.IsNullOrEmpty(name))
				throw new Exception("Name must not be empty or null.");
			if (IsNameInUse(name))
				throw new Exception(String.Format("The name \"{0}\" is already in use.", name));
			if (IsEmailInUse(email))
				throw new Exception(String.Format("The e-mail \"{0}\" is already in use.", email));
			if (IsIPBanned(ip))
				throw new Exception(String.Format("The IP {0} is banned.", ip));
			if (IsEmailBanned(email))
				throw new Exception(String.Format("The e-mail {0} is banned.", email));
			var creationDate = DateTime.UtcNow;
			var authorizationKey = Guid.NewGuid();
			var salt = Guid.NewGuid();
			var hashedPassword = password.GetMD5Hash(salt);
			var user = _userRepository.CreateUser(name, email, creationDate, isApproved, hashedPassword, authorizationKey, salt, userType, departmentId);
			_securityLogService.CreateLogEntry(null, user, ip, String.Empty, SecurityLogType.UserCreated);
			return user;
		}
        public User UpdateUser(int UserId, string name, string password,  bool isApproved, int userType, int departmentId)
        {
            var creationDate = DateTime.UtcNow;
            var authorizationKey = Guid.NewGuid();
            var salt = Guid.NewGuid();
            var hashedPassword = password.GetMD5Hash(salt);
            var user = _userRepository.UpdateUser(UserId,name, creationDate, isApproved, hashedPassword, authorizationKey, salt, userType, departmentId);

            return user;
        }

        public void DeleteUser(User targetUser, User user, string ip, bool ban)
		{
			if (ban)
				_banRepository.BanEmail(targetUser.Email);
			_userRepository.DeleteUser(targetUser);
			_securityLogService.CreateLogEntry(user, targetUser, ip, String.Format("Name: {0}, E-mail: {1}", targetUser.Name, targetUser.Email), SecurityLogType.UserDeleted);
		}

		public void UpdateLastActicityDate(User user)
		{
			user.LastActivityDate = DateTime.UtcNow;
			_userRepository.UpdateLastActivityDate(user, user.LastActivityDate);
		}

		public void ChangeEmail(User targetUser, string newEmail, User user, string ip)
		{
			ChangeEmail(targetUser, newEmail, user, ip, _settingsManager.Current.IsNewUserApproved);
		}

		public void ChangeEmail(User targetUser, string newEmail, User user, string ip, bool isUserApproved)
		{
			if (!newEmail.IsEmailAddress())
				throw new Exception("E-mail address invalid.");
			if (IsEmailInUse(newEmail))
				throw new Exception(String.Format("The e-mail \"{0}\" is already in use.", newEmail));
			var oldEmail = targetUser.Email;
			_userRepository.ChangeEmail(targetUser, newEmail);
			targetUser.Email = newEmail;
			_userRepository.UpdateIsApproved(targetUser, isUserApproved);
			_securityLogService.CreateLogEntry(user, targetUser, ip, String.Format("Old: {0}, New: {1}", oldEmail, newEmail), SecurityLogType.EmailChange);
		}

		public void ChangeName(User targetUser, string newName, User user, string ip)
		{
			if (String.IsNullOrEmpty(newName))
				throw new Exception("Name must not be empty or null.");
			if (IsNameInUse(newName))
				throw new Exception(String.Format("The name \"{0}\" is already in use.", newName));
			var oldName = targetUser.Name;
			_userRepository.ChangeName(targetUser, newName);
			targetUser.Name = newName;
			_securityLogService.CreateLogEntry(user, targetUser, ip, String.Format("Old: {0}, New: {1}", oldName, newName), SecurityLogType.NameChange);
		}

		public void UpdateIsApproved(User targetUser, bool isApproved, User user, string ip)
		{
			if (targetUser == null)
				throw new ArgumentNullException("targetUser");
			_userRepository.UpdateIsApproved(targetUser, isApproved);
			var logType = isApproved ? SecurityLogType.IsApproved : SecurityLogType.IsNotApproved;
			_securityLogService.CreateLogEntry(user, targetUser, ip, String.Empty, logType);
		}

		public void UpdateAuthorizationKey(User user, Guid key)
		{
			if (user == null)
				throw new ArgumentNullException("user");
			_userRepository.UpdateAuthorizationKey(user, key);
		}

		public User SetupUserViewData(IPrincipal principal, ViewDataDictionary viewData)
		{
			User user = null;
			if (principal != null)
			{
				user = GetUserByName(principal.Identity.Name);
				viewData.SetUserInViewData(user);
			}
			return user;
		}

		public void Logout(User user, string ip)
		{
			_formsAuthWrapper.SignOut();
			_securityLogService.CreateLogEntry(null, user, ip, String.Empty, SecurityLogType.Logout);
		}

		public bool Login(string email, string password, bool persistCookie, HttpContextBase context)
		{
			Guid? salt;
			var result = CheckPassword(email, password, out salt);
			if (result)
			{
				var user = GetUserByEmail(email);
				_formsAuthWrapper.SetAuthCookie(context, user, persistCookie);
				user.LastLoginDate = DateTime.UtcNow;
				_userRepository.UpdateLastLoginDate(user, user.LastLoginDate);
				_securityLogService.CreateLogEntry(null, user, context.Request.UserHostAddress, String.Empty, SecurityLogType.Login);
				if (!salt.HasValue)
					SetPassword(user, password, context.Request.UserHostAddress, user);
			}
			else
				_securityLogService.CreateLogEntry((User) null, null, context.Request.UserHostAddress, "E-mail attempted: " + email, SecurityLogType.FailedLogin);
			return result;
		}

		public void Login(User user, HttpContextBase context)
		{
			Login(user, false, context);
		}

		public void Login(User user, bool persistCookie, HttpContextBase context)
		{
			_formsAuthWrapper.SetAuthCookie(context, user, persistCookie);
			user.LastLoginDate = DateTime.UtcNow;
			_userRepository.UpdateLastLoginDate(user, user.LastLoginDate);
			_securityLogService.CreateLogEntry(null, user, context.Request.UserHostAddress, String.Empty, SecurityLogType.Login);
		}

		public List<string> GetAllRoles()
		{
			return _roleRepository.GetAllRoles();
		}

		public void CreateRole(string role, User user, string ip)
		{
			_roleRepository.CreateRole(role);
			_securityLogService.CreateLogEntry(user, null, ip, "Role: " + role, SecurityLogType.RoleCreated);
		}

		public void DeleteRole(string role, User user, string ip)
		{
			if (role.ToLower() == PermanentRoles.Admin.ToLower() || role.ToLower() == PermanentRoles.Moderator.ToLower())
				throw new InvalidOperationException("Can't delete Admin or Moderator roles.");
			_roleRepository.DeleteRole(role);
			_securityLogService.CreateLogEntry(user, null, ip, "Role: " + role, SecurityLogType.RoleDeleted);
		}

		public User VerifyAuthorizationCode(Guid key, string ip)
		{
			var targetUser = _userRepository.GetUserByAuthorizationKey(key);
			if (targetUser == null)
				return null;
			var newKey = Guid.NewGuid();
			UpdateAuthorizationKey(targetUser, newKey);
			UpdateIsApproved(targetUser, true, null, ip);
			targetUser.AuthorizationKey = newKey;
			return targetUser;
		}

		public List<User> SearchByEmail(string email)
		{
			return _userRepository.SearchByEmail(email);
		}
        public List<User> GetUserListByType(string type)
        {
            return _userRepository.GetUserListByType(type);
        }

        public List<User> SearchByName(string name)
		{
			return _userRepository.SearchByName(name);
		}

		public List<User> SearchByRole(string role)
		{
			return _userRepository.SearchByRole(role);
		}

		public UserEdit GetUserEdit(User user)
		{
			if (user == null)
				throw new ArgumentNullException("user");
			var profile = _profileRepository.GetProfile(user.UserID);
			return new UserEdit(user, profile);
		}

		public void EditUser(User targetUser, UserEdit userEdit, bool removeAvatar, bool removePhoto, HttpPostedFileBase avatarFile, HttpPostedFileBase photoFile, string ip, User user)
		{
			if (!String.IsNullOrWhiteSpace(userEdit.NewEmail))
				ChangeEmail(targetUser, userEdit.NewEmail, user, ip, userEdit.IsApproved);
			if (!String.IsNullOrWhiteSpace(userEdit.NewPassword))
				SetPassword(targetUser, userEdit.NewPassword, ip, user);
			if (targetUser.IsApproved != userEdit.IsApproved)
				UpdateIsApproved(targetUser, userEdit.IsApproved, user, ip);

			var profile = _profileRepository.GetProfile(targetUser.UserID);
			profile.IsSubscribed = userEdit.IsSubscribed;
			profile.ShowDetails = userEdit.ShowDetails;
			profile.IsPlainText = userEdit.IsPlainText;
			profile.HideVanity = userEdit.HideVanity;
			profile.TimeZone = userEdit.TimeZone;
			profile.IsDaylightSaving = userEdit.IsDaylightSaving;
			profile.Signature = _textParsingService.ForumCodeToHtml(userEdit.Signature);
			profile.Location = userEdit.Location;
			profile.Dob = userEdit.Dob;
			profile.Web = userEdit.Web;
			profile.Aim = userEdit.Aim;
			profile.Icq = userEdit.Icq;
			profile.YahooMessenger = userEdit.YahooMessenger;
			profile.Facebook = userEdit.Facebook;
			profile.Twitter = userEdit.Twitter;
			if (removeAvatar)
				profile.AvatarID = null;
			if (removePhoto)
				profile.ImageID = null;
			_profileRepository.Update(profile);

			var newRoles = userEdit.Roles ?? new string[0];
			_roleRepository.ReplaceUserRoles(targetUser.UserID, newRoles);
			foreach (var role in targetUser.Roles)
				if (!newRoles.Contains(role))
					_securityLogService.CreateLogEntry(user, targetUser, ip, role, SecurityLogType.UserRemovedFromRole);
			foreach (var role in newRoles)
				if (!targetUser.Roles.Contains(role))
					_securityLogService.CreateLogEntry(user, targetUser, ip, role, SecurityLogType.UserAddedToRole);

			if (avatarFile != null && avatarFile.ContentLength > 0)
			{
				var avatarID = _userAvatarRepository.SaveNewAvatar(targetUser.UserID, avatarFile.GetBytes(), DateTime.UtcNow);
				profile.AvatarID = avatarID;
				_profileRepository.Update(profile);
			}

			if (photoFile != null && photoFile.ContentLength > 0)
			{
				var imageID = _userImageRepository.SaveNewImage(targetUser.UserID, 0, true, photoFile.GetBytes(), DateTime.UtcNow);
				profile.ImageID = imageID;
				_profileRepository.Update(profile);
			}
		}

		public void EditUserProfileImages(User user, bool removeAvatar, bool removePhoto, HttpPostedFileBase avatarFile, HttpPostedFileBase photoFile)
		{
			var profile = _profileRepository.GetProfile(user.UserID);
			if (removeAvatar)
			{
				_userAvatarRepository.DeleteAvatarsByUserID(user.UserID);
				profile.AvatarID = null;
			}
			if (removePhoto)
			{
				_userImageRepository.DeleteImagesByUserID(user.UserID);
				profile.ImageID = null;
			}
			_profileRepository.Update(profile);

			if (avatarFile != null && avatarFile.ContentLength > 0)
			{
				_userAvatarRepository.DeleteAvatarsByUserID(user.UserID);
				var bytes = _imageService.ConstrainResize(avatarFile.GetBytes(), _settingsManager.Current.UserAvatarMaxWidth, _settingsManager.Current.UserAvatarMaxHeight, 70);
				var avatarID = _userAvatarRepository.SaveNewAvatar(user.UserID, bytes, DateTime.UtcNow);
				profile.AvatarID = avatarID;
				_profileRepository.Update(profile);
			}

			if (photoFile != null && photoFile.ContentLength > 0)
			{
				_userImageRepository.DeleteImagesByUserID(user.UserID);
				var bytes = _imageService.ConstrainResize(photoFile.GetBytes(), _settingsManager.Current.UserImageMaxWidth, _settingsManager.Current.UserImageMaxHeight, 70);
				var imageID = _userImageRepository.SaveNewImage(user.UserID, 0, _settingsManager.Current.IsNewUserImageApproved, bytes, DateTime.UtcNow);
				profile.ImageID = imageID;
				_profileRepository.Update(profile);
			}
		}
		
		// TODO: this and some other stuff probably belongs in ProfileService
		public void EditUserProfile(User user, UserEditProfile userEditProfile)
		{
			var profile = _profileRepository.GetProfile(user.UserID);
			if (profile == null)
				throw new Exception(String.Format("No profile found for UserID {0}", user.UserID));
			profile.IsSubscribed = userEditProfile.IsSubscribed;
			profile.ShowDetails = userEditProfile.ShowDetails;
			profile.IsPlainText = userEditProfile.IsPlainText;
			profile.HideVanity = userEditProfile.HideVanity;
			profile.TimeZone = userEditProfile.TimeZone;
			profile.IsDaylightSaving = userEditProfile.IsDaylightSaving;
			profile.Signature = _textParsingService.ForumCodeToHtml(userEditProfile.Signature);
			profile.Location = userEditProfile.Location;
			profile.Dob = userEditProfile.Dob;
			profile.Web = userEditProfile.Web;
			profile.Aim = userEditProfile.Aim;
			profile.Icq = userEditProfile.Icq;
			profile.YahooMessenger = userEditProfile.YahooMessenger;
			profile.Facebook = userEditProfile.Facebook;
			profile.Twitter = userEditProfile.Twitter;
			_profileRepository.Update(profile);
		}

		public bool VerifyPassword(User user, string password)
		{
			Guid? salt;
			string hashedPassword;
			var savedHashedPassword = _userRepository.GetHashedPasswordByEmail(user.Email, out salt);
			if (salt.HasValue)
				hashedPassword = password.GetMD5Hash(salt.Value);
			else
				hashedPassword = password.GetMD5Hash();
			return hashedPassword == savedHashedPassword;
		}

		public bool IsPasswordValid(string password, ModelStateDictionary modelStateDictionary)
		{
			var result = true;
			if (String.IsNullOrEmpty(password) || password.Length < 6)
			{
				modelStateDictionary.AddModelError("Password", "Password must be at least six characters");
				result = false;
			}
			return result;
		}

		public List<User> GetUsersOnline()
		{
			return _userRepository.GetUsersOnline();
		}

		public int GetTotalUsers()
		{
			return _userRepository.GetTotalUsers();
		}

		public void GeneratePasswordResetEmail(User user, string resetLink)
		{
			if (user == null)
				throw new ArgumentNullException("user");
			var newAuth = Guid.NewGuid();
			UpdateAuthorizationKey(user, newAuth);
			user.AuthorizationKey = newAuth;
			var link = resetLink + "/" + newAuth;
			_forgotPasswordMailer.ComposeAndQueue(user, link);
		}

		public void ResetPassword(User user, string newPassword, HttpContextBase httpContext)
		{
			SetPassword(user, newPassword, httpContext.Request.UserHostAddress, null);
			UpdateAuthorizationKey(user, Guid.NewGuid());
			Login(user, httpContext);
		}

		public List<User> GetSubscribedUsers()
		{
			return _userRepository.GetSubscribedUsers();
		}

		public Dictionary<User, int> GetUsersByPointTotals(int top)
		{
			return _userRepository.GetUsersByPointTotals(top);
		}
	}
}