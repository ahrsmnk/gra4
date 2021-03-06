﻿using AutoMapper.QueryableExtensions;
using GRA.Domain.Model;
using GRA.Domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GRA.Data.Repository
{
    public class UserRepository
        : AuditingRepository<Model.User, User>, IUserRepository
    {
        private readonly Security.Abstract.IPasswordHasher _passwordHasher;
        public UserRepository(ServiceFacade.Repository repositoryFacade,
            ILogger<UserRepository> logger,
            Security.Abstract.IPasswordHasher passwordHasher) : base(repositoryFacade, logger)
        {
            _passwordHasher = Require.IsNotNull(passwordHasher, nameof(passwordHasher));
        }

        public async Task AddRoleAsync(int currentUserId, int userId, int roleId)
        {
            var userLookup = await DbSet
                .AsNoTracking()
                .Where(_ => _.Id == userId && _.IsDeleted == false)
                .SingleOrDefaultAsync();

            if(userLookup == null)
            {
                throw new GraException($"Unable to add roles to user {userId}.");
            }

            var userRoleAssignment = new Model.UserRole
            {
                UserId = userLookup.Id,
                RoleId = roleId,
                CreatedBy = currentUserId,
                CreatedAt = DateTime.Now
            };
            await _context.UserRoles.AddAsync(userRoleAssignment);
        }

        public async Task SetUserPasswordAsync(int currentUserId, int userId, string password)
        {
            var user = DbSet.Find(userId);
            string original = _entitySerializer.Serialize(user);
            user.PasswordHash = _passwordHasher.HashPassword(password);
            await UpdateSaveAsync(currentUserId, user, original);
        }
        public async Task<User> GetByUsernameAsync(string username)
        {
            var lookupUser = await DbSet
                .AsNoTracking()
                .Where(_ => _.Username == username && _.IsDeleted == false)
                .SingleOrDefaultAsync();
            if (lookupUser != null)
            {
                return _mapper.Map<Model.User, User>(lookupUser);
            }
            else
            {
                return null;
            }
        }

        public async Task<AuthenticationResult> AuthenticateUserAsync(string username,
            string password)
        {
            var result = new AuthenticationResult
            {
                FoundUser = false,
                PasswordIsValid = false
            };

            var lookupUser = await DbSet
                .Where(_ => _.Username == username && _.IsDeleted == false)
                .SingleOrDefaultAsync();
            if (lookupUser != null)
            {
                result.FoundUser = true;
                result.PasswordIsValid =
                    _passwordHasher.VerifyHashedPassword(lookupUser.PasswordHash, password);
                if (result.PasswordIsValid)
                {
                    result.User = _mapper.Map<Model.User, User>(lookupUser);
                    lookupUser.LastAccess = DateTime.Now;
                    await SaveAsync();
                }
            }
            return result;
        }

        public async Task<IEnumerable<User>> PageAllAsync(int siteId,
            int skip,
            int take,
            string search = null,
            SortUsersBy sortBy = SortUsersBy.LastName)
        {
            var userList = DbSet
                .AsNoTracking()
                .Include(_ => _.Branch)
                .Include(_ => _.Program)
                .Include(_ => _.System);

            IQueryable<Model.User> filteredUserList = null;
            if (string.IsNullOrEmpty(search))
            {
                filteredUserList = userList.Where(_ => _.IsDeleted == false
                    && _.SiteId == siteId);
            }
            else
            {
                filteredUserList = userList.Where(_ => _.IsDeleted == false
                    && _.SiteId == siteId
                    && (_.Username.Contains(search)
                        || _.FirstName.Contains(search)
                        || _.LastName.Contains(search)
                        || _.Email.Contains(search)
                        || (_.FirstName + " " + _.LastName).Contains(search)
                        || (_.LastName + " " + _.FirstName).Contains(search)));
            }

            switch (sortBy)
            {
                case SortUsersBy.FirstName:
                    filteredUserList = filteredUserList
                        .OrderBy(_ => _.FirstName)
                        .ThenBy(_ => _.LastName)
                        .ThenBy(_ => _.Username);
                    break;
                case SortUsersBy.LastName:
                    filteredUserList = filteredUserList
                        .OrderBy(_ => _.LastName)
                        .ThenBy(_ => _.FirstName)
                        .ThenBy(_ => _.Username);
                    break;
                case SortUsersBy.RegistrationDate:
                    filteredUserList = filteredUserList
                        .OrderBy(_ => _.CreatedAt)
                        .ThenBy(_ => _.LastName)
                        .ThenBy(_ => _.FirstName)
                        .ThenBy(_ => _.Username);
                    break;
                case SortUsersBy.Username:
                    filteredUserList = filteredUserList
                        .OrderBy(_ => _.Username)
                        .ThenBy(_ => _.LastName)
                        .ThenBy(_ => _.FirstName);
                    break;
            }

            return await filteredUserList
                .Skip(skip)
                .Take(take)
                .ProjectTo<User>()
                .ToListAsync();
        }

        public async Task<int> GetCountAsync(int siteId, string search = null)
        {
            IQueryable<Model.User> userCount = null;
            if (string.IsNullOrEmpty(search))
            {
                userCount = DbSet
                    .AsNoTracking()
                    .Where(_ => _.IsDeleted == false && _.SiteId == siteId);
            }
            else
            {
                userCount = DbSet
                    .AsNoTracking()
                    .Where(_ => _.IsDeleted == false && _.SiteId == siteId
                        && (_.Username.Contains(search)
                        || _.FirstName.Contains(search)
                        || _.LastName.Contains(search)
                        || _.Email.Contains(search)));
            }

            return await userCount.CountAsync();
        }

        public async Task<int> GetCountAsync(StatusSummary request)
        {
            IQueryable<Model.User> userCount = null;
            userCount = DbSet
                .AsNoTracking()
                .Where(_ => _.IsDeleted == false && _.SiteId == request.SiteId);

            if (request.StartDate != null)
            {
                userCount = userCount.Where(_ => _.CreatedAt >= request.StartDate);
            }

            if (request.EndDate != null)
            {
                userCount = userCount.Where(_ => _.CreatedAt <= request.EndDate);
            }

            if(request.ProgramId != null)
            {
                userCount = userCount.Where(_ => _.ProgramId == request.ProgramId);
            }

            if(request.SystemId != null)
            {
                userCount = userCount.Where(_ => _.SystemId == request.SystemId);
            }

            if(request.BranchId != null)
            {
                userCount = userCount.Where(_ => _.BranchId == request.BranchId);
            }

            return await userCount.CountAsync();
        }

        public async override Task RemoveSaveAsync(int userId, int id)
        {
            var entity = await _context.Users
                .Where(_ => _.IsDeleted == false && _.Id == id)
                .SingleAsync();
            entity.IsDeleted = true;
            await base.UpdateAsync(userId, entity, null);
            await base.SaveAsync();
        }

        public async Task<IEnumerable<User>>
            PageHouseholdAsync(int householdHeadUserId, int skip, int take)
        {
            return await DbSet
                .AsNoTracking()
                .Include(_ => _.Branch)
                .Include(_ => _.Program)
                .Include(_ => _.System)
                .Where(_ => _.IsDeleted == false
                       && _.HouseholdHeadUserId == householdHeadUserId)
                .Skip(skip)
                .Take(take)
                .ProjectTo<User>()
                .ToListAsync();
        }

        public async Task<int> GetHouseholdCountAsync(int householdHeadUserId)
        {
            return await DbSet
                .AsNoTracking()
                .Where(_ => _.IsDeleted == false
                       && _.HouseholdHeadUserId == householdHeadUserId)
                       .CountAsync();
        }

        public override async Task<User> GetByIdAsync(int id)
        {
            return await DbSet
                .AsNoTracking()
                .Include(_ => _.Branch)
                .Include(_ => _.Program)
                .Include(_ => _.System)
                .Include(_ => _.EnteredSchool)
                .Where(_ => _.Id == id && _.IsDeleted == false)
                .ProjectTo<User>()
                .SingleOrDefaultAsync();
        }

        public async Task<DataWithId<IEnumerable<string>>> GetUserIdAndUsernames(string email)
        {
            var userIdLookup = await DbSet
                .AsNoTracking()
                .Where(_ => _.Email == email && _.IsDeleted == false)
                .FirstOrDefaultAsync();

            if (userIdLookup == null)
            {
                return null;
            }

            return new DataWithId<IEnumerable<string>>
            {
                Id = userIdLookup.Id,
                Data = await DbSet
                    .AsNoTracking()
                    .Where(_ => _.Email == email 
                        && !string.IsNullOrEmpty(_.Username)
                        && _.IsDeleted == false)
                    .Select(_ => _.Username)
                    .ToListAsync()
            };
        }

        public async Task<IEnumerable<int>> GetAllUserIds(int siteId)
        {
            return await DbSet
                .AsNoTracking()
                .Where(_ => _.SiteId == siteId && _.IsDeleted == false)
                .Select(_ => _.Id)
                .ToListAsync();
        }

        public async Task<IEnumerable<User>> GetHouseholdAsync(int householdHeadUserId)
        {
            return await DbSet
                .AsNoTracking()
                .Include(_ => _.Branch)
                .Include(_ => _.Program)
                .Include(_ => _.System)
                .Where(_ => _.IsDeleted == false
                       && _.HouseholdHeadUserId == householdHeadUserId)
                .ProjectTo<User>()
                .ToListAsync();
        }
    }
}
