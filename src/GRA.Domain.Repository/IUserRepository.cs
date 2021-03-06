﻿using GRA.Domain.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GRA.Domain.Repository
{
    public interface IUserRepository : IRepository<User>
    {
        Task AddRoleAsync(int currentUserId, int userId, int roleId);
        Task<AuthenticationResult> AuthenticateUserAsync(string username, string password);
        Task<IEnumerable<int>> GetAllUserIds(int siteId);
        Task<User> GetByUsernameAsync(string username);
        Task<int> GetCountAsync(int siteId, string search = null);
        Task<int> GetCountAsync(StatusSummary request);
        Task<int> GetHouseholdCountAsync(int householdHeadUserId);
        Task<DataWithId<IEnumerable<string>>> GetUserIdAndUsernames(string email);
        Task<IEnumerable<Model.User>> PageAllAsync(int siteId, 
            int skip, 
            int take, 
            string search = null,
            SortUsersBy sortBy = SortUsersBy.LastName);
        Task<IEnumerable<Model.User>>
            PageHouseholdAsync(int householdHeadUserId, int skip, int take);
        Task SetUserPasswordAsync(int currentUserId, int userId, string password);
        Task<IEnumerable<User>> GetHouseholdAsync(int householdHeadUserId);
    }
}
