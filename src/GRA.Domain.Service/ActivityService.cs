﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GRA.Domain.Repository;
using GRA.Domain.Model;
using GRA.Domain.Service.Abstract;
using System.Collections.Generic;
using GRA.Abstract;
using System;

namespace GRA.Domain.Service
{
    public class ActivityService : Abstract.BaseUserService<UserService>
    {
        private readonly IBadgeRepository _badgeRepository;
        private readonly IBookRepository _bookRepository;
        private readonly IChallengeRepository _challengeRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly IPointTranslationRepository _pointTranslationRepository;
        private readonly IProgramRepository _programRepository;
        private readonly ITriggerRepository _triggerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUserLogRepository _userLogRepository;
        private readonly IVendorCodeRepository _vendorCodeRepository;
        private readonly IVendorCodeTypeRepository _vendorCodeTypeRepository;
        private readonly MailService _mailService;

        private ICollection<int> _queuedTriggerIds;

        public ActivityService(ILogger<UserService> logger,
            IUserContextProvider userContext,
            IBadgeRepository badgeRepository,
            IBookRepository bookRepository,
            IChallengeRepository challengeRepository,
            INotificationRepository notificationRepository,
            IPointTranslationRepository pointTranslationRepository,
            IProgramRepository programRepository,
            ITriggerRepository triggerRepository,
            IUserRepository userRepository,
            IUserLogRepository userLogRepository,
            IVendorCodeRepository vendorCodeRepository,
            IVendorCodeTypeRepository vendorCodeTypeRepository,
            MailService mailService) : base(logger, userContext)
        {
            _badgeRepository = Require.IsNotNull(badgeRepository, nameof(badgeRepository));
            _bookRepository = Require.IsNotNull(bookRepository, nameof(bookRepository));
            _challengeRepository = Require.IsNotNull(challengeRepository,
                nameof(challengeRepository));
            _notificationRepository = Require.IsNotNull(notificationRepository,
                nameof(notificationRepository));
            _pointTranslationRepository = Require.IsNotNull(pointTranslationRepository,
                nameof(pointTranslationRepository));
            _programRepository = Require.IsNotNull(programRepository, nameof(programRepository));
            _triggerRepository = Require.IsNotNull(triggerRepository, nameof(triggerRepository));
            _userRepository = Require.IsNotNull(userRepository, nameof(userRepository));
            _userLogRepository = Require.IsNotNull(userLogRepository,
                nameof(userLogRepository));
            _vendorCodeRepository = Require.IsNotNull(vendorCodeRepository,
                nameof(vendorCodeRepository));
            _vendorCodeTypeRepository = Require.IsNotNull(vendorCodeTypeRepository,
                nameof(vendorCodeTypeRepository));
            _mailService = Require.IsNotNull(mailService, nameof(mailService));
        }

        public async Task<ActivityLogResult> LogActivityAsync(int userIdToLog,
            int activityAmountEarned,
            Book book = null)
        {
            VerifyCanLog();

            if (activityAmountEarned < 0)
            {
                throw new GraException($"Cannot log negative activity amounts!");
            }

            if (book != null)
            {
                if (string.IsNullOrWhiteSpace(book.Title)
                    && !string.IsNullOrWhiteSpace(book.Author))
                {
                    throw new GraException("When providing an author you must also provide a title.");
                }
            }

            int activeUserId = GetActiveUserId();
            int authUserId = GetClaimId(ClaimType.UserId);
            var userToLog = await _userRepository.GetByIdAsync(userIdToLog);

            bool loggingAsAdminUser = HasPermission(Permission.LogActivityForAny);

            if (activeUserId != userIdToLog
                && authUserId != userToLog.HouseholdHeadUserId
                && !loggingAsAdminUser)
            {
                string error = $"User id {activeUserId} cannot log activity for user id {userIdToLog}";
                _logger.LogError(error);
                throw new GraException(error);
            }

            var translation
                = await _pointTranslationRepository.GetByProgramIdAsync(userToLog.ProgramId);

            int pointsEarned
                = (activityAmountEarned / translation.ActivityAmount) * translation.PointsEarned;

            // add the row to the user's point log
            var userLog = new UserLog
            {
                ActivityEarned = activityAmountEarned,
                IsDeleted = false,
                PointsEarned = pointsEarned,
                UserId = userToLog.Id,
                PointTranslationId = translation.Id
            };
            if (activeUserId != userToLog.Id)
            {
                userLog.AwardedBy = activeUserId;
            }
            userLog = await _userLogRepository.AddSaveAsync(activeUserId, userLog);

            // update the score in the user record
            userToLog = await AddPointsSaveAsync(authUserId,
                activeUserId,
                userToLog.Id,
                pointsEarned);

            // prepare the notification text
            string activityDescription = "for <strong>";
            if (translation.TranslationDescriptionPresentTense.Contains("{0}"))
            {
                activityDescription += string.Format(
                    translation.TranslationDescriptionPresentTense,
                    userLog.ActivityEarned);
                if (userLog.ActivityEarned == 1)
                {
                    activityDescription += $" {translation.ActivityDescription}";
                }
                else
                {
                    activityDescription += $" {translation.ActivityDescriptionPlural}";
                }
            }
            else
            {
                activityDescription = $"{translation.TranslationDescriptionPresentTense} {translation.ActivityDescription}";
            }
            activityDescription += "</strong>";

            // create the notification record
            var notification = new Notification
            {
                PointsEarned = pointsEarned,
                Text = $"<span class=\"fa fa-star\"></span> You earned <strong>{pointsEarned} points</strong> {activityDescription}!",
                UserId = userToLog.Id
            };

            int? bookId = null;
            // add the book if one was supplied
            if (book != null && !string.IsNullOrWhiteSpace(book.Title))
            {
                bookId = await AddBookAsync(GetActiveUserId(), book);
                notification.Text += $" The book <strong><em>{book.Title}</em></strong> by <strong>{book.Author}</strong> was added to your book list.";
            }

            await _notificationRepository.AddSaveAsync(authUserId, notification);

            return new ActivityLogResult
            {
                UserLogId = userLog.Id,
                BookId = bookId
            };
        }

        public async Task<User> RemoveActivityAsync(int userIdToLog,
            int userLogIdToRemove)
        {
            int activeUserId = GetActiveUserId();
            var activeUser = await _userRepository.GetByIdAsync(activeUserId);
            int authUserId = GetClaimId(ClaimType.UserId);

            if (userIdToLog == activeUserId
                || activeUser.HouseholdHeadUserId == authUserId
                || HasPermission(Permission.LogActivityForAny))
            {
                var userLog = await _userLogRepository.GetByIdAsync(userLogIdToRemove);

                int pointsToRemove = userLog.PointsEarned;
                await _userLogRepository.RemoveSaveAsync(authUserId, userLogIdToRemove);
                return await RemovePointsSaveAsync(authUserId, userIdToLog, pointsToRemove);
            }
            else
            {
                string error = $"User id {authUserId} cannot remove activity for user id {userIdToLog}";
                _logger.LogError(error);
                throw new GraException(error);
            }
        }

        public async Task<int> AddBookAsync(int userId, Book book)
        {
            VerifyCanLog();
            int activeUserId = GetActiveUserId();
            var activeUser = await _userRepository.GetByIdAsync(activeUserId);
            int authUserId = GetClaimId(ClaimType.UserId);

            if (userId == activeUserId
                || activeUser.HouseholdHeadUserId == authUserId
                || HasPermission(Permission.LogActivityForAny))
            {
                return await _bookRepository.AddSaveForUserAsync(activeUserId, userId, book);
            }
            else
            {
                _logger.LogError($"User {activeUserId} doesn't have permission to add a book for {userId}.");
                throw new GraException("Permission denied.");
            }
        }

        public async Task RemoveBookAsync(int userId, int bookId)
        {
            int requestedByUserId = GetClaimId(ClaimType.UserId);
            if (requestedByUserId == userId
                || HasPermission(Permission.LogActivityForAny))
            {
                await _bookRepository.RemoveForUserAsync(requestedByUserId, userId, bookId);
            }
            else
            {
                _logger.LogError($"User {requestedByUserId} doesn't have permission to remove a book for {userId}.");
                throw new GraException("Permission denied.");
            }

        }

        public async Task UpdateBookAsync(int userId, Book book)
        {
            int requestedByUserId = GetClaimId(ClaimType.UserId);
            if (requestedByUserId == userId
                || HasPermission(Permission.LogActivityForAny))
            {
                await _bookRepository.UpdateSaveAsync(requestedByUserId, book);
            }
            else
            {
                _logger.LogError($"User {requestedByUserId} doesn't have permission to edit a book for {userId}.");
                throw new GraException("Permission denied.");
            }
        }

        public async Task<bool> UpdateChallengeTasksAsync(int challengeId,
            IEnumerable<ChallengeTask> challengeTasks)
        {
            VerifyCanLog();
            int activeUserId = GetActiveUserId();
            int authUserId = GetClaimId(ClaimType.UserId);

            var challenge = await _challengeRepository.GetActiveByIdAsync(challengeId, activeUserId);

            if (challenge.IsCompleted == true)
            {
                _logger.LogError($"User {authUserId} cannot make changes to a completed challenge {challengeId}.");
                throw new GraException("Challenge is already completed.");
            }

            var updateStatuses = await _challengeRepository.UpdateUserChallengeTasksAsync(activeUserId,
                challengeTasks);

            // re-fetch challenge with tasks completed
            challenge = await _challengeRepository.GetActiveByIdAsync(challengeId, activeUserId);

            // loop tasks to see if we need to perform any additional point translation/book tasks
            foreach (var updateStatus in updateStatuses)
            {
                var challengeTaskDetails = challenge.Tasks.Where(_ => _.Id == updateStatus.ChallengeTask.Id).SingleOrDefault();
                // is there work we need to do on this item
                if (challengeTaskDetails.ActivityCount != null
                    && challengeTaskDetails.PointTranslationId != null)
                {
                    // did something change?
                    _logger.LogDebug($"Challenge task {updateStatus.ChallengeTask.Id} counts as an activity");
                    if (updateStatus.WasComplete != updateStatus.IsComplete)
                    {
                        _logger.LogDebug($"Status of {updateStatus.ChallengeTask.Id}: was {updateStatus.WasComplete}, is {updateStatus.IsComplete}");
                        if (updateStatus.IsComplete)
                        {
                            // person completed the task
                            Book book = null;
                            if (challengeTaskDetails.ChallengeTaskType == ChallengeTaskType.Book)
                            {
                                _logger.LogDebug($"Challenge task {updateStatus.ChallengeTask.Id} is a book");
                                book = new Book
                                {
                                    Title = updateStatus.ChallengeTask.Title,
                                    Author = updateStatus.ChallengeTask.Author,
                                    ChallengeId = challenge.Id
                                };
                            }
                            _logger.LogDebug($"Logging activity for {activeUserId} based on challenge task {updateStatus.ChallengeTask.Id}");
                            var userLogResult = await LogActivityAsync(activeUserId,
                                (int)challengeTaskDetails.ActivityCount,
                                book);

                            // update record with user log result
                            _logger.LogDebug($"Update success, recording UserLogId {userLogResult.UserLogId} and BookId {userLogResult.BookId}");
                            await _challengeRepository.UpdateUserChallengeTaskAsync(activeUserId,
                                updateStatus.ChallengeTask.Id,
                                userLogResult.UserLogId,
                                userLogResult.BookId);
                        }
                        if (updateStatus.WasComplete)
                        {
                            // person un-completed the task
                            // unwind the points they earned
                            var challengeTaskInfo = await _challengeRepository
                                .GetUserChallengeTaskResultAsync(activeUserId,
                                    updateStatus.ChallengeTask.Id);
                            if (challengeTaskInfo == null)
                            {
                                _logger.LogError($"Unable to unwind points for {activeUserId} on {updateStatus.ChallengeTask.Id} - no UserLogId recorded");
                            }
                            else
                            {
                                _logger.LogDebug($"Unwinding points for {activeUserId} earned in UserLogId {challengeTaskInfo.UserLogId}");
                                await RemoveActivityAsync(activeUserId, challengeTaskInfo.UserLogId);
                                // remove the title
                                if (challengeTaskDetails.ChallengeTaskType == ChallengeTaskType.Book
                                    && challengeTaskInfo.BookId != null)
                                {
                                    _logger.LogDebug($"Removing for {activeUserId} book registration {challengeTaskInfo.BookId}");
                                    await RemoveBookAsync(activeUserId, (int)challengeTaskInfo.BookId);
                                }
                            }
                        }
                    }
                }
            }

            int pointsAwarded = (int)challenge.PointsAwarded;
            int completedTasks = challengeTasks.Where(_ => _.IsCompleted == true).Count();
            if (completedTasks >= challenge.TasksToComplete)
            {
                var userLog = new UserLog
                {
                    IsDeleted = false,
                    PointsEarned = pointsAwarded,
                    UserId = activeUserId,
                    ChallengeId = challengeId
                };
                if (challenge.BadgeId != null)
                {
                    userLog.BadgeId = challenge.BadgeId;
                }
                await _userLogRepository.AddSaveAsync(activeUserId, userLog);

                // update the score in the user record
                var postUpdateUser = await AddPointsSaveAsync(authUserId,
                    activeUserId,
                    activeUserId,
                    pointsAwarded);

                string badgeNotification = null;
                Badge badge = await AwardBadgeAsync(activeUserId, challenge.BadgeId);
                if (badge != null)
                {
                    badgeNotification = $" and a badge";
                }

                // create the notification record
                var notification = new Notification
                {
                    PointsEarned = pointsAwarded,
                    Text = $"<span class=\"fa fa-star\"></span> You earned <strong>{pointsAwarded} points{badgeNotification}</strong> for completing the challenge: <strong>{challenge.Name}</strong>!",
                    UserId = activeUserId,
                    ChallengeId = challengeId
                };
                if (badge != null)
                {
                    notification.BadgeId = challenge.BadgeId;
                    notification.BadgeFilename = badge.Filename;
                };

                await _notificationRepository.AddSaveAsync(authUserId, notification);

                return true;
            }
            else
            {
                return false;
            }
        }

        private async Task<User> AddPointsSaveAsync(int authUserId,
            int activeUserId,
            int whoEarnedUserId,
            int pointsEarned)
        {
            if (pointsEarned < 0)
            {
                throw new GraException($"Cannot log negative points!");
            }

            var earnedUser = await _userRepository.GetByIdAsync(whoEarnedUserId);
            if (earnedUser == null)
            {
                throw new Exception($"Could not find a user with id {whoEarnedUserId}");
            }

            earnedUser.PointsEarned += pointsEarned;
            earnedUser.IsActive = true;
            earnedUser.LastActivityDate = DateTime.Now;

            // update the user's achiever status if they've crossed the threshhold
            var program = await _programRepository.GetByIdAsync(earnedUser.ProgramId);

            if (!earnedUser.IsAchiever
                && earnedUser.PointsEarned >= program.AchieverPointAmount)
            {
                earnedUser.IsAchiever = true;

                var notification = new Notification
                {
                    PointsEarned = 0,
                    Text = $"<span class=\"fa fa-certificate\"></span> Congratulations! You've achieved <strong>{program.AchieverPointAmount} points</strong> reaching the goal of the program!",
                    UserId = earnedUser.Id,
                    IsAchiever = true
                };

                var badge = await AwardBadgeAsync(activeUserId, program.AchieverBadgeId);

                if (badge != null)
                {
                    await _userLogRepository.AddAsync(activeUserId, new UserLog
                    {
                        UserId = whoEarnedUserId,
                        PointsEarned = 0,
                        IsDeleted = false,
                        BadgeId = badge.Id,
                        Description = $"You reached the goal of {program.AchieverPointAmount} points!"
                    });
                    notification.Text += " You've also earned a badge!";
                    notification.BadgeId = badge.Id;
                    notification.BadgeFilename = badge.Filename;
                }

                await _notificationRepository.AddSaveAsync(authUserId, notification);
            }

            // save user's changes
            if (activeUserId == earnedUser.Id
                || authUserId == earnedUser.HouseholdHeadUserId)
            {
                earnedUser = await _userRepository.UpdateSaveNoAuditAsync(earnedUser);
            }
            else
            {
                earnedUser = await _userRepository.UpdateSaveAsync(activeUserId, earnedUser);
            }

            await AwardTriggersAsync(earnedUser.Id);

            return earnedUser;
        }

        public async Task<PointTranslation> GetUserPointTranslationAsync()
        {
            var user = await _userRepository.GetByIdAsync(GetActiveUserId());
            return await _pointTranslationRepository.GetByIdAsync(user.ProgramId);
        }

        private async Task<User>
            RemovePointsSaveAsync(int currentUserId,
            int removePointsFromUserId,
            int pointsToRemove)
        {
            if (pointsToRemove < 0)
            {
                throw new GraException($"Cannot remove negative points!");
            }

            var removeUser = await _userRepository.GetByIdAsync(removePointsFromUserId);

            if (removeUser == null)
            {
                throw new Exception($"Could not find single user with id {removePointsFromUserId}");
            }

            removeUser.PointsEarned -= pointsToRemove;

            // update the user's achiever status if they've crossed the threshhold
            var program = await _programRepository.GetByIdAsync(removeUser.ProgramId);

            if (removeUser.PointsEarned >= program.AchieverPointAmount)
            {
                removeUser.IsAchiever = true;
            }
            else
            {
                removeUser.IsAchiever = false;
            }

            return await _userRepository.UpdateSaveAsync(currentUserId, removeUser);
        }

        private async Task<Badge> AwardBadgeAsync(int userId, int? badgeId)
        {
            Badge badge = null;
            if (badgeId != null)
            {
                badge = await _badgeRepository.GetByIdAsync((int)badgeId);
                await _badgeRepository.AddUserBadge(userId, (int)badgeId);
            }
            return badge;
        }

        private async Task AwardTriggersAsync(int userId)
        {
            // load the initial list of triggers that might have been achieved
            var triggers = await _triggerRepository.GetTriggersAsync(userId);
            do
            {
                if (_queuedTriggerIds == null || _queuedTriggerIds.Count() == 0)
                {
                    // this is our first check, we're not nested
                    _queuedTriggerIds = triggers.Select(_ => _.Id).ToList();
                }
                else
                {
                    // we've already checked triggers so don't double-award any that are queued
                    triggers = triggers
                        .Where(_ => !_queuedTriggerIds.Contains(_.Id))
                        .ToList();

                    // update the queue with everything we're working on
                    _queuedTriggerIds = _queuedTriggerIds
                        .Union(triggers.Select(_ => _.Id))
                        .ToList();
                }

                // if any triggers came back let's check them
                while (triggers.Count() > 0)
                {
                    // pull the first trigger off the list and remove it from the list
                    var trigger = triggers.First();
                    triggers.Remove(trigger);

                    // add that we've processed this trigger for this user
                    await _triggerRepository.AddTriggerActivationAsync(userId, trigger.Id);

                    // if there are points to be awarded, do that now
                    if (trigger.AwardPoints > 0)
                    {
                        // this call will recursively call this method in case any additional
                        // point-based triggers are fired by this action
                        await AddPointsSaveAsync(GetClaimId(ClaimType.UserId),
                            GetActiveUserId(),
                            userId,
                            trigger.AwardPoints);
                    }

                    // every trigger awards a badge
                    var badge = await AwardBadgeAsync(userId, trigger.AwardBadgeId);

                    // log the notification
                    await _notificationRepository.AddSaveAsync(userId, new Notification
                    {
                        PointsEarned = trigger.AwardPoints,
                        UserId = userId,
                        Text = trigger.AwardMessage,
                        BadgeId = trigger.AwardBadgeId,
                        BadgeFilename = badge.Filename
                    });

                    // add the award to the user's history
                    await _userLogRepository.AddSaveAsync(userId, new UserLog
                    {
                        UserId = userId,
                        PointsEarned = trigger.AwardPoints,
                        IsDeleted = false,
                        BadgeId = trigger.AwardBadgeId,
                        Description = trigger.AwardMessage
                    });

                    // award any vendor code that is necessary
                    await AwardVendorCodeAsync(userId, trigger.AwardVendorCodeTypeId);

                    // remove this item from the queued list of triggers
                    _queuedTriggerIds.Remove(trigger.Id);
                }

                // reload the list in case a trigger triggered another trigger :rage4:
                triggers = await _triggerRepository.GetTriggersAsync(userId);
            } while (triggers.Count() > 0);
        }

        private async Task AwardVendorCodeAsync(int userId, int? vendorCodeTypeId)
        {
            if (vendorCodeTypeId != null)
            {
                var codeType = await _vendorCodeTypeRepository.GetByIdAsync((int)vendorCodeTypeId);
                try
                {
                    var assignedCode = await _vendorCodeRepository.AssignCodeAsync((int)vendorCodeTypeId, userId);
                    await _mailService.SendSystemMailAsync(new Mail
                    {
                        ToUserId = userId,
                        CanParticipantDelete = false,
                        Subject = codeType.MailSubject,
                        Body = codeType.Mail.Contains("{Code}")
                            ? codeType.Mail.Replace("{Code}", assignedCode.Code)
                            : codeType.Mail + " " + assignedCode.Code
                    });
                }
                catch (Exception)
                {
                    await _mailService.SendSystemMailAsync(new Mail
                    {
                        ToUserId = userId,
                        CanParticipantDelete = true,
                        Subject = codeType.MailSubject,
                        Body = codeType.Mail.Contains("{Code}")
                            ? codeType.Mail.Replace("{Code}", $"{codeType.Description} not available - please contact us.")
                            : codeType.Mail + " " + $"{codeType.Description} not available - please contact us."
                    });

                    // TODO let admin know that vendor code assignment didn't work?
                }
            }
        }

        public async Task LogSecretCodeAsync(int userIdToLog, string secretCode)
        {
            VerifyCanLog();

            if(string.IsNullOrWhiteSpace(secretCode))
            {
                throw new GraException("You must enter a code!");
            }

            int activeUserId = GetActiveUserId();
            int authUserId = GetClaimId(ClaimType.UserId);
            var userToLog = await _userRepository.GetByIdAsync(userIdToLog);

            bool loggingAsAdminUser = HasPermission(Permission.LogActivityForAny);

            if (activeUserId != userIdToLog
                && authUserId != userToLog.HouseholdHeadUserId
                && !loggingAsAdminUser)
            {
                string error = $"User id {activeUserId} cannot log a code for user id {userIdToLog}";
                _logger.LogError(error);
                throw new GraException("You do not have permission to apply that code.");
            }

            var trigger = await _triggerRepository.GetByCodeAsync(GetCurrentSiteId(), secretCode);

            if(trigger == null)
            {
                throw new GraException($"<strong>{secretCode}</strong> is not a valid code.");
            }

            // check if this user's gotten this code
            var alreadyDone 
                = await _triggerRepository.CheckTriggerActivationAsync(userIdToLog, trigger.Id);
            if(alreadyDone != null)
            {
                throw new GraException($"You already entered the code <strong>{secretCode}</strong> on <strong>{alreadyDone:d}</strong>!");
            }


            // add that we've processed this trigger for this user
            await _triggerRepository.AddTriggerActivationAsync(userIdToLog, trigger.Id);

            // every trigger awards a badge
            var badge = await AwardBadgeAsync(userIdToLog, trigger.AwardBadgeId);

            // log the notification
            await _notificationRepository.AddSaveAsync(authUserId, new Notification
            {
                PointsEarned = trigger.AwardPoints,
                UserId = userIdToLog,
                Text = trigger.AwardMessage,
                BadgeId = trigger.AwardBadgeId,
                BadgeFilename = badge.Filename
            });

            // add the award to the user's history
            var userLog = new UserLog
            {
                UserId = userIdToLog,
                PointsEarned = trigger.AwardPoints,
                IsDeleted = false,
                BadgeId = trigger.AwardBadgeId,
                Description = trigger.AwardMessage
            };

            if (activeUserId != userToLog.Id)
            {
                userLog.AwardedBy = activeUserId;
            }

            await _userLogRepository.AddSaveAsync(authUserId, userLog);

            // award any vendor code that is necessary
            await AwardVendorCodeAsync(userIdToLog, trigger.AwardVendorCodeTypeId);

            // if there are points to be awarded, do that now, also check for other triggers
            if (trigger.AwardPoints > 0)
            {
                await AddPointsSaveAsync(authUserId,
                    activeUserId,
                    userIdToLog,
                    trigger.AwardPoints);
            }
            else
            {
                await AwardTriggersAsync(userIdToLog);
            }
        }
    }
}