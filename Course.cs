using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace LinguaLeoDownloader
{
	[XmlRoot("Course", Namespace = "urn:schemas-lingualeo-com:course")]
	public class Course
	{
		private static readonly ILog log = Logger.GetLogger<Course>();
		internal static Lazy<XmlSerializer> serializer = new Lazy<XmlSerializer>(() =>
			CourseProgram.CreateSerializer<Course>());
		private CourseLesson[] lessons;

		internal DownloadManager Downloader { get; private set; }

		[XmlIgnore]
		public CourseProgram Owner { get; internal set; }

		[XmlIgnore]
		public Uri URI { get; private set; }

		[XmlAttribute, DefaultValue(0)]
		public int ID { get; set; }

		[XmlIgnore]
		public string FileName { get; set; }

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

		[XmlAttribute, DefaultValue(LanguageLevel.Unknown)]
		public LanguageLevel Level { get; set; }

		[XmlAttribute("Picture")]
		public string PicFile { get; set; }

		public string PicPath { get { return Path.Combine(Owner.ImagesPath, PicFile); } }

		public string BasePath { get { return Path.Combine(Owner.BasePath, FileName ?? ID.ToString()); } }

		[XmlElement("Lesson")]
		public CourseLesson[] Lessons
		{
			get { return lessons; }
			set
			{
				lessons = value ?? new CourseLesson[0];
				for (int k = 0; k < lessons.Length; k++)
				{
					var lesson = lessons[k];
					lesson.Owner = this;
					lesson.Sort = k + 1;
				}
			}
		}

		public int FirstLesson
		{
			get
			{
				var page = lessons[0].Pages.FirstOrDefault();
				return page == null ? 0 : page.ID;
			}
		}

		[XmlIgnore]
		public int? Progress { get; private set; }

		public Course(CourseProgram owner, string url)
		{
			this.Owner = owner;
			this.URI = new Uri(Owner.URI, url);
			this.Downloader = new DownloadManager(this.URI);
		}

		public Course(string file, string url)
		{
			this.Owner = new CourseProgram(url)
			{
				Name = "WebTranslateIt"
			};
			this.URI = this.Owner.URI;
			this.FileName = file;
			this.Downloader = new DownloadManager(this.URI);
		}

		public Course() { }

		public void Parse(JSONDictionary course)
		{
			ID = course.Get("id", 0);
			Name = course["name"];
			Description = course["desc"];
			int group = course.Get("group", 0);
			if (group > 0)
				Level = (LanguageLevel)(1 << (group - 1));
			Progress = course.Get<int>("progress");

			log.Info("Course Name: {0}; Level: {1}; Progress: {2}%", Name, Level, Progress);
			//log.Info("Course Description: {0}", Description);

			PicFile = Path.GetFileName(course["pic_url"]);

			log.Debug("Icon: {0}", Path.GetFileName(PicPath));

			if (!File.Exists(PicPath))
			{
				log.Info("Download image for {0}", Name);
				Downloader.DownloadFile(course["pic_url"], PicPath);
			}

			var program = Downloader.DownloadCourse();
			var courses = program.Child("courses");
			var data = courses.Child(ID);

			var list = data.Child("lesson");
			lessons = list.Values.Select(item =>
				{
					try
					{
						var lesson = new CourseLesson(this, item["CourseLessonId", 0]);
						lesson.Parse(item);
						return lesson;
					}
					catch (Exception ex)
					{
						log.Error(ex.Message);
						return null;
					}
				}).Where(lesson => lesson != null && lesson.Type == 0).OrderBy(lesson => lesson.Sort).ToArray();
		}

		public void ParseJSON(JSONDictionary data, string[] order)
		{
			var course = data.Child("course");
			var pages = data.Child("item");
			var list = data.Child("lesson");

			Name = course["name"];
			Description = course["description"];

			log.Info("Course Name: {0}", Name);
			//log.Info("Course Description: {0}", Description);

			int n;
			var iorder = order.Where(id => int.TryParse(id, out n)).Select(id => int.Parse(id)).ToArray();

			var lessons = list.Children.Select(item =>
				{
					var lesson = new CourseLesson(this, item.Key);
					lesson.ParseJSON(item.Value);
					return lesson;
				}).OrderBy(lesson => (uint)Array.IndexOf(iorder, lesson.ID)).ToArray();

			int last = lessons[0].ID;
			var child = new Dictionary<int, int>();
			var parent = lessons.Select(lesson => lesson.ID).ToArray();
			foreach (int id in iorder)
			{
				if (parent.Contains(id))
					last = id;
				else
					child[id] = last;
			}

			var children = pages.Children.Select(item =>
				{
					var page = new CoursePage(lessons[0]) { ID = item.Key };
					page.ParseJSON(item.Value);
					return page;
				}).Where(page => page.Rule != null).ToArray();

			last = lessons[0].ID;
			foreach (var lesson in lessons)
				lesson.pages = children.Where(page =>
					child.ContainsKey(page.ID) ? child[page.ID] == lesson.ID : lesson.ID == last)
					.OrderBy(page => (uint)Array.IndexOf(iorder, page.ID)).ToArray();
			this.lessons = lessons.Where(lesson => lesson.pages.Length > 0).ToArray();
		}

		public void Render()
		{
			if (!Directory.Exists(BasePath))
				Directory.CreateDirectory(BasePath);

			foreach (var lesson in lessons)
				foreach (var page in lesson.Pages)
				{
					var render = new PageRender(Owner, this, lesson, page);
					string path = Path.Combine(BasePath, "Lesson_" + page.ID + ".html");
					log.Debug("Write " + path);
					using (var fs = new FileStream(path, FileMode.CreateNew))
					using (var writer = new StreamWriter(fs, Encoding.UTF8))
						render.Render("Lesson.html", writer);
				}
		}
	}
}
