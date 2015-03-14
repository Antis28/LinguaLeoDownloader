using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace LinguaLeoDownloader
{
	public class CoursePage
	{
		private static readonly ILog log = Logger.GetLogger<CoursePage>();
		internal static Lazy<XmlSerializer> serializer = new Lazy<XmlSerializer>(() =>
			new XmlSerializer(typeof(CoursePage)));

		[XmlIgnore]
		public CourseLesson Owner { get; internal set; }

		[XmlAttribute]
		public int ID { get; set; }

		[XmlElement]
		public string Name { get; set; }

		[XmlAttribute(DataType = "date")]
		public DateTime LastUpdate { get; set; }

		public bool LastUpdateSpecified { get { return LastUpdate != DateTime.MinValue; } }

		[XmlIgnore]
		public string Rule { get; private set; }

		[XmlElement("Rule")]
		public XmlNode RuleText
		{
			get
			{
				return Rule.ToXML();
			}
			set
			{
				Rule = value.Value;
			}
		}

		public bool RuleTextSpecified { get { return Rule != null; } }

		[XmlIgnore]
		public int Type { get; private set; }

		[XmlIgnore]
		public int? Sort { get; internal set; }

		[XmlIgnore]
		public int? Progress { get; private set; }

		public string ImagesPath { get { return Path.Combine(Owner.Owner.BasePath, "images"); } }

		public bool HasName { get { return Name != null; } }

		public int PrevPage
		{
			get
			{
				int last = 0;
				foreach(var page in Owner.Owner.Lessons.Where(lesson => lesson.Pages != null)
					.SelectMany(lesson => lesson.Pages))
				{
					if (page == this) return last;
					last = page.ID;
				}
				return 0;
			}
		}

		public int NextPage
		{
			get
			{
				bool found = false;
				foreach (var page in Owner.Owner.Lessons.Where(lesson => lesson.Pages != null)
					.SelectMany(lesson => lesson.Pages))
				{
					if (found) return page.ID;
					found = page == this;
				}
				return 0;
			}
		}

		public bool HasPrev { get { return PrevPage != 0; } }

		public bool HasNext { get { return NextPage != 0; } }

		public CoursePage(CourseLesson owner)
		{
			this.Owner = owner;
		}

		public CoursePage() { }

		public void Parse(JSONDictionary page)
		{
			ID = page["CourseItemId", 0];
			LastUpdate = page["LastUpdate", DateTime.MinValue];
			Rule = page["FieldText"];
			Sort = page.Get<int>("CourseItemSort");
			Type = page["CourseItemType", 0];
			Progress = page.Get<int>("progress");
			ResolveLinks();
		}

		public void ParseJSON(JSONDictionary page)
		{
			Name = page["title"];
			var field = page.Child("field");
			Rule = field["text"];
			ResolveLinks();
		}

		private void ResolveLinks()
		{
			if (Rule == null) return;
			for (int k = Rule.IndexOf("src=\""); k > 0; k = Rule.IndexOf("src=\"", k + 6))
			{
				int start = k + 5;
				int end = Rule.IndexOf("\"", start);
				string src = Rule.Substring(start, end - start);
				if (src.Length == 0 || src == "#") continue;

				if (!Directory.Exists(ImagesPath))
					Directory.CreateDirectory(ImagesPath);

				string local = Path.Combine(ImagesPath, Path.GetFileName(src));
				if (!File.Exists(local))
				{
					Owner.Downloader.DownloadFile(src, local);
				}

				string img = "images/" + Path.GetFileName(src);
				Rule = Rule.Remove(start, end - start).Insert(start, img);
			}
		}
	}
}
