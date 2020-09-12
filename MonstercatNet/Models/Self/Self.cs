﻿using System;

namespace SoftThorn.MonstercatNet
{
    public sealed class Self
    {
        public bool Admin { get; set; }
        public DateTime Birthday { get; set; }
        public DateTime CreatedAt { get; set; }
        public string DiscordId { get; set; }
        public string Email { get; set; }
        public object[] EmailOptins { get; set; }
        public string EmailVerificationStatus { get; set; }
        public bool FreeGold { get; set; }
        public string GoogleMapsPlaceId { get; set; }
        public bool HasDownload { get; set; }
        public bool HasGold { get; set; }
        public string Id { get; set; }
        public string LastSeen { get; set; }
        public int MaxLicenses { get; set; }
        public string PlaceName { get; set; }
        public string PlaceNameFull { get; set; }
        public string RealName { get; set; }
        public Settings Settings { get; set; }
        public Subscription Subscription { get; set; }
        public string TwoFactorState { get; set; }
        public string Username { get; set; }
    }
}
