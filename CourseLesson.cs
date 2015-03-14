using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace LinguaLeoDownloader
{
	public class CourseLesson
	{
		private static readonly ILog log = Logger.GetLogger<CourseLesson>();
		internal static Lazy<XmlSerializer> serializer = new Lazy<XmlSerializer>(() =>
			new XmlSerializer(typeof(CourseLesson)));
		internal CoursePage[] pages;

		internal DownloadManager Downloader { get; private set; }

		[XmlIgnore]
		public Course Owner { get; internal set; }

		[XmlIgnore]
		public Uri URI { get; private set; }

		[XmlAttribute]
		public int ID { get; set; }

		[XmlElement]
		public string Name { get; set; }

		[XmlIgnore]
		public string Description { get; private set; }

		[XmlElement("Description")]
		public XmlNode DescriptionText
		{
			get
			{
				return Description.ToXML();
			}
			set
			{
				Description = value != null ? value.Value : null;
			}
		}

		public bool DescriptionTextSpecified { get { return Description != null; } }

		[XmlIgnore]
		public int? Sort { get; internal set; }

		[XmlIgnore]
		public int Type { get; private set; }

		[XmlIgnore]
		public int? Progress { get; private set; }

		[XmlElement("Page")]
		public CoursePage[] Pages
		{
			get { return pages; }
			set
			{
				pages = value ?? new CoursePage[0];
				for (int k = 0; k < pages.Length; k++)
				{
					var page = pages[k];
					page.Owner = this;
					page.Sort = k + 1;
				}
			}
		}

		public CourseLesson(Course owner, int id)
		{
			this.Owner = owner;
			this.ID = id;
			this.URI = new Uri(Owner.URI, String.Format("{1}/lesson/{0}/info", this.ID, Owner.ID));
			this.Downloader = new DownloadManager(this.URI);
		}

		public CourseLesson() { }

		public void Parse(JSONDictionary lesson)
		{
			Name = lesson["CourseLessonName"];
			Description = lesson["CourseLessonDesc"];
			Sort = lesson.Get<int>("CourseLessonSort");
			Type = lesson["CourseLessonType", 0];
			Progress = lesson.Get<int>("progress");

			log.Info("Lesson Name: {0}; Progress: {1}%", Name, Progress);
			//log.Info("Lesson Description: {0}", Description);

			var program = Downloader.DownloadCourse();
			var lessons = program.Child("lesson");
			var data = lessons.Child(ID);

			var list = data.Child("item");
			pages = list.Values.Select(item =>
				{
					try
					{
						var page = new CoursePage(this);
						page.Parse(item);
						return page;
					}
					catch (Exception ex)
					{
						log.Error(ex.Message);
						return null;
					}
				}).Where(page => page != null && page.Type == 11).OrderBy(page => page.Sort).ToArray();
		}

		public void ParseJSON(JSONDictionary lesson)
		{
			Name = lesson["name"];
			Description = lesson["description"];

			log.Info("Lesson Name: {0}", Name);
			//log.Info("Lesson Description: {0}", Description);
		}
	}
}
