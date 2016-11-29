﻿using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using GRA.Domain.Repository;
using System.Threading.Tasks;

namespace GRA.Domain.Service
{
    public class ConfigurationService : Abstract.BaseService<ConfigurationService>
    {
        private readonly IBranchRepository branchRepository;
        private readonly IChallengeRepository challengeRepository;
        private readonly IChallengeTaskRepository challengeTaskRepository;
        private readonly IProgramRepository programRepository;
        private readonly IRoleRepository roleRepository;
        private readonly ISiteRepository siteRepository;
        private readonly ISystemRepository systemRepository;
        private readonly IUserRepository userRepository;
        private readonly IPointTranslationRepository pointTranslationRepository;
        public ConfigurationService(ILogger<ConfigurationService> logger,
            IBranchRepository branchRepository,
            IChallengeRepository challengeRepository,
            IChallengeTaskRepository challengeTaskRepository,
            IProgramRepository programRepository,
            IRoleRepository roleRepository,
            ISiteRepository siteRepository,
            ISystemRepository systemRepository,
            IUserRepository userRepository,
            IPointTranslationRepository pointTranslationRepository) : base(logger)
        {
            this.branchRepository = Require.IsNotNull(branchRepository, nameof(branchRepository));
            this.challengeRepository = Require.IsNotNull(challengeRepository,
                nameof(challengeRepository));
            this.challengeTaskRepository = Require.IsNotNull(challengeTaskRepository,
                nameof(challengeTaskRepository));
            this.programRepository = Require.IsNotNull(programRepository,
                nameof(programRepository));
            this.roleRepository = Require.IsNotNull(roleRepository, nameof(roleRepository));
            this.siteRepository = Require.IsNotNull(siteRepository, nameof(siteRepository));
            this.systemRepository = Require.IsNotNull(systemRepository, nameof(systemRepository));
            this.userRepository = Require.IsNotNull(userRepository, nameof(userRepository));
            this.pointTranslationRepository = Require.IsNotNull(pointTranslationRepository,
                nameof(pointTranslationRepository));
        }

        public async Task<bool> NeedsInitialSetupAsync()
        {
            var firstSite = await siteRepository.PageAllAsync(0, 1);
            return firstSite.Count() == 0;
        }

        public async Task<Model.User> InitialSetupAsync(Model.User adminUser, string password)
        {
            var topSites = await siteRepository.PageAllAsync(0, 1);

            if (topSites.Count() > 0)
            {
                throw new Exception("Can't perform initial setup with existing sites: found existing sites in the database.");
            }

            var site = new Model.Site
            {
                Name = "Default site",
                Path = "default"
            };
            // create default site
            site = await siteRepository.AddSaveAsync(-1, site);

            if (site == null)
            {
                throw new Exception("Unable to add initial default site or multiple sites found.");
            }

            var system = new Model.System
            {
                SiteId = site.Id,
                Name = "Maricopa County Library District"
            };
            system = await systemRepository.AddSaveAsync(-1, system);

            var branch = new Model.Branch
            {
                SiteId = site.Id,
                SystemId = system.Id,
                Name = "Admin",
                Address = "2700 N. Central Ave Ste 700, 85004",
                Telephone = "602-652-3064",
                Url = "http://mcldaz.org/"
            };
            branch = await branchRepository.AddSaveAsync(-1, branch);

            var program = new Model.Program
            {
                SiteId = site.Id,
                AchieverPointAmount = 1000,
                Name = "Winter Reading Program"
            };
            program = await programRepository.AddSaveAsync(-1, program);

            adminUser.BranchId = branch.Id;
            adminUser.ProgramId = program.Id;
            adminUser.SiteId = site.Id;
            adminUser.SystemId = system.Id;
            var user = await userRepository.AddSaveAsync(0, adminUser);
            await userRepository.SetUserPasswordAsync(user.Id, password);

            int creatorUserId = user.Id;

            site.CreatedBy = creatorUserId;
            site = await siteRepository.UpdateSaveAsync(creatorUserId, site);

            system.CreatedBy = creatorUserId;
            system = await systemRepository.UpdateSaveAsync(creatorUserId, system);

            branch.CreatedBy = creatorUserId;
            branch = await branchRepository.UpdateSaveAsync(creatorUserId, branch);

            program.CreatedBy = creatorUserId;
            program = await programRepository.UpdateSaveAsync(creatorUserId, program);

            var pointTranslation = new Model.PointTranslation
            {
                ActivityAmount = 1,
                ActivityDescription = "book",
                IsSingleEvent = true,
                PointsEarned = 10,
                ProgramId = program.Id,
                TranslationName = "One book, ten points"
            };
            await pointTranslationRepository.AddSaveAsync(creatorUserId, pointTranslation);

            var adminRole = await roleRepository.AddSaveAsync(creatorUserId, new Model.Role
            {
                Name = "System Administrator"
            });

            await userRepository.AddRoleAsync(creatorUserId, user.Id, adminRole.Id);

            foreach (var value in Enum.GetValues(typeof(Model.Permission)))
            {
                await roleRepository.AddPermissionAsync(creatorUserId, value.ToString());
            }
            await roleRepository.SaveAsync();

            foreach (var value in Enum.GetValues(typeof(Model.Permission)))
            {
                await roleRepository.AddPermissionToRoleAsync(creatorUserId,
                    adminRole.Id,
                    value.ToString());
            }
            await roleRepository.SaveAsync();

            foreach (var value in Enum.GetValues(typeof(Model.ChallengeTaskType)))
            {
                await challengeTaskRepository.AddChallengeTaskTypeAsync(creatorUserId,
                    value.ToString());
            }
            await challengeRepository.SaveAsync();

            // sample challenge

            var challenge = new Model.Challenge
            {
                SiteId = site.Id,
                RelatedSystemId = system.Id,
                Name = "Test challenge",
                Description = "This is a test challenge!",
                IsActive = false,
                IsDeleted = false,
                PointsAwarded = 10,
                TasksToComplete = 2,
                RelatedBranchId = branch.Id
            };

            challenge = await challengeRepository.AddSaveAsync(creatorUserId, challenge);

            int positionCounter = 1;
            await challengeTaskRepository.AddSaveAsync(creatorUserId, new Model.ChallengeTask
            {
                ChallengeId = challenge.Id,
                Title = "Be excellent to each other",
                ChallengeTaskType = Model.ChallengeTaskType.Action,
                Position = positionCounter++
            });
            await challengeTaskRepository.AddSaveAsync(creatorUserId, new Model.ChallengeTask
            {
                ChallengeId = challenge.Id,
                Title = "Party on, dudes!",
                ChallengeTaskType = Model.ChallengeTaskType.Action,
                Position = positionCounter++
            });
            await challengeTaskRepository.AddSaveAsync(creatorUserId, new Model.ChallengeTask
            {
                ChallengeId = challenge.Id,
                Title = "Slaughterhouse-Five, or The Children's Crusade: A Duty-Dance with Death",
                Author = "Kurt Vonnegut",
                Isbn = "978-0385333849",
                ChallengeTaskType = Model.ChallengeTaskType.Book,
                Position = positionCounter++
            });

            return user;
        }
    }
}