﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace GRA.Domain.Model
{
    public class Page : Abstract.BaseDomainEntity
    {
        public int SiteId { get; set; }
        [Required]
        public string Title { get; set; }
        public string Description { get; set; }
        [Required]
        public string Stub { get; set; }
        [Required]
        public string Content { get; set; }
        [DisplayName("Show in Footer")]
        public bool IsFooter { get; set; }
        [DisplayName("Publish this page")]
        public bool IsPublished { get; set; }
        
    }
}