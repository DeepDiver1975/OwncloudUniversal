﻿using System;

namespace owncloud_universal.WebDav
{
    public class DavItem
    {
        public string Href { get; set; }
        public DateTime? CreationDate { get; set; }
        public string Etag { get; set; }
        public bool IsHidden { get; set; }
        public bool IsCollection { get; set; }
        public string ContentType { get; set; }
        public DateTime? LastModified { get; set; }
        public string DisplayName { get; set; }
        public int? ContentLength { get; set; }
    }
}
