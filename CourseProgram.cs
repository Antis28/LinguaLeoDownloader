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
	[XmlRoot("Program", Namespace = "urn:schemas-lingualeo-com:course")]
	public class CourseProgram
	{
		private static readonly ILog log = Logger.GetLogger<CourseProgram>();
		internal static Lazy<XmlSerializer> serializer = new Lazy<XmlSerializer>(() =>
			CreateSerializer<CourseProgram>());
		internal static Lazy<XmlSerializerNamespaces> namespaces = new Lazy<XmlSerializerNamespaces>(() =>
			new XmlSerializerNamespaces(new XmlQualifiedName[] { new XmlQualifiedName(String.Empty, "urn:schemas-lingualeo-com:course") }));

		internal DownloadManager Downloader { get; private set; }

		[XmlIgnore]
		public Uri URI { get; private set; }

		[XmlElement]
		public string Name { get; set; }

		[XmlAttribute]
		public string Type { get; set; }

		[XmlAttribute]
		public string Author { get; set; }

		[XmlAttribute, DefaultValue(LanguageLevel.Unknown)]
		public LanguageLevel Level { get; set; }

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

		public string BasePath
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "LinguaLeo", Name ?? "Unknown Courses");
			}
		}

		public string ImagesPath { get { return Path.Combine(BasePath, "images"); } }

		[XmlAttribute("Icon")]
		public string IconFile { get; set; }

		public string IconPath { get { return Path.Combine(ImagesPath, IconFile); } }

		[XmlAttribute("Cover")]
		public string CoverFile { get; set; }

		public string CoverPath { get { return Path.Combine(ImagesPath, CoverFile); } }

		[XmlIgnore]
		public Course[] Courses { get; private set; }

		[XmlElement("Course")]
		public CourseIndex[] CoursesIndex
		{
			get { return Courses.Select(course => new CourseIndex(course)).ToArray(); }
			set { Courses = (value ?? new CourseIndex[0]).Select(index => index.Resolve(BasePath, this)).ToArray(); }
		}

		[XmlIgnore]
		public int? Progress { get; private set; }

		public class CourseIndex
		{
			private Course course;

			[XmlAttribute]
			public int ID { get; set; }

			[XmlElement]
			public string Name { get { return course.Name; } set { } }

			[XmlAttribute]
			public LanguageLevel Level { get { return course.Level; } set { } }

			[XmlElement]
			public XmlNode Description { get { return course.DescriptionText; } set { } }

			public bool DescriptionSpecified { get { return course.DescriptionTextSpecified; } }

			[XmlAttribute("Picture")]
			public string PicFile { get { return course.PicFile; } set { } }

			public CourseIndex(Course course)
			{
				this.course = course;
				this.ID = course.ID;
				try
				{
					string xml = Path.Combine(course.Owner.BasePath, course.ID + ".xml");
					using (var writer = new FileStream(xml, FileMode.CreateNew))
					{
						Course.serializer.Value.Serialize(writer, course, CourseProgram.namespaces.Value);
					}
				}
				catch (Exception ex)
				{
					log.Error(ex.Message);
				}
			}

			public CourseIndex() { }

			public Course Resolve(string path, CourseProgram owner)
			{
				string xml = Path.Combine(path, ID + ".xml");
				log.Debug("Open " + xml);
				using (var reader = new FileStream(xml, FileMode.Open))
				{
					course = (Course)Course.serializer.Value.Deserialize(reader);
					course.Owner = owner;
				}
				return course;
			}
		}

		public CourseProgram(string url)
		{
			this.URI = new Uri(url);
			this.Downloader = new DownloadManager(this.URI);
		}

		public CourseProgram() { }

		internal static XmlSerializer CreateSerializer<TObject>()
		{
			var ser = new XmlSerializer(typeof(TObject));
			ser.UnknownAttribute += SerializerUnknownAttribute;
			ser.UnknownElement += SerializerUnknownElement;
			ser.UnknownNode += SerializerUnknownNode;
			return ser;
		}

		internal static void SerializerUnknownNode(object sender, XmlNodeEventArgs e)
		{
			throw new InvalidDataException(String.Format("Unknown node {0} at {1}:{2}", e.Name, e.LineNumber, e.LinePosition));
		}

		internal static void SerializerUnknownElement(object sender, XmlElementEventArgs e)
		{
			throw new InvalidDataException(String.Format("Unknown element {0} at {1}:{2} expected {3}", e.Element.Name, e.LineNumber, e.LinePosition, e.ExpectedElements));
		}

		internal static void SerializerUnknownAttribute(object sender, XmlAttributeEventArgs e)
		{
			throw new InvalidDataException(String.Format("Unknown attribute {0} at {1}:{2} expected {3}", e.Attr.Name, e.LineNumber, e.LinePosition, e.ExpectedAttributes));
		}

		public void Parse()
		{
			var data = Downloader.DownloadCourse();

			var program = data.Items("programs")[0];

			Name = program["titleText"];
			Type = program["type"];
			Author = program["author"];

			Description = program["description"];
			Progress = program.Get<int>("progress");

			log.Info("Program: {0}; Author: {1}; Level: {2}; Progress: {3}%", Name, Author, Level, Progress);
			//log.Info("Description: {0}", Description);

			IconFile = Path.GetFileName(program["icon"]);
			CoverFile = Path.GetFileName(program["cover"]);

			log.Debug("Icon: {0}; Cover: {1}", Path.GetFileName(IconPath), Path.GetFileName(CoverPath));

			if (!Directory.Exists(ImagesPath))
				Directory.CreateDirectory(ImagesPath);

			if (!File.Exists(IconPath))
			{
				log.Info("Download course icon");
				Downloader.DownloadFile(program["icon"], IconPath);
			}
			if (!File.Exists(CoverPath))
			{
				log.Info("Download course cover");
				Downloader.DownloadFile(program["cover"], CoverPath);
			}

			var list = data.Child("courses");
			Courses = list.Values.Select(item =>
				{
					try
					{
						var course_data = item.Child("data");
						var course = new Course(this, course_data["url"]);
						course.Parse(course_data);
						return course;
					}
					catch (Exception ex)
					{
						log.Error(ex.Message);
						return null;
					}
				}).Where(course => course != null).OrderBy(course => course.Level).ToArray();

			int level = program["level", 0];
			int children = Courses.Select(course => course.Level).Distinct().Cast<int>().Sum();
			if (children > 0)
				Level = (LanguageLevel)children;
			else if (level > 0)
				Level = (LanguageLevel)(1 << (level - 1));
		}

		public void Render()
		{
			var render = new PageRender(this);
			string path = Path.Combine(BasePath, "Course.html");
			log.Debug("Write " + path);
			using (var fs = new FileStream(path, FileMode.CreateNew))
			using (var writer = new StreamWriter(fs, Encoding.UTF8))
				render.Render("Course.html", writer);

			foreach (var course in Courses)
				course.Render();
		}
	}
}
