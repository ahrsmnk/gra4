﻿using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace GRA.Domain.Model
{
    public class Event : Abstract.BaseDomainEntity
    {
        public int SiteId { get; set; }
        [Required]
        public int RelatedSystemId { get; set; }
        [Required]
        public int RelatedBranchId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; }

        [DisplayName("Starts At")]
        [Required]
        public DateTime StartsAt { get; set; }

        [DisplayName("All Day Event")]
        [Required]
        public bool AllDay { get; set; }

        [Required]
        [MaxLength(1500)]
        public string Description { get; set; }

        [Required]
        public bool IsActive { get; set; }
        [Required]
        public bool IsValid { get; set; }

        [DisplayName("Event Link")]
        [MaxLength(300)]
        public string ExternalLink { get; set; }

        [DisplayName("At Branch")]
        public int? AtBranchId { get; set; }

        [DisplayName("At Location")]
        public int? AtLocationId { get; set; }

        [DisplayName("For Program")]
        public int? ProgramId { get; set; }
        public int? ParentEventId { get; set; }

        public string EventLocationName { get; set; }
        public string EventLocationAddress { get; set; }
        public string EventLocationLink { get; set; }
        public string EventLocationTelephone { get; set; }
    }
}
