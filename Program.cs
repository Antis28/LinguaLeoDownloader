using System;
using System.Collections.Generic;
using System.IO;

namespace LinguaLeoDownloader
{
	class Program
	{
		private static readonly ILog log = Logger.GetLogger<Program>();
		static void Main(string[] args)
		{
			if (args.Length != 1 && !(args.Length == 2 && Path.GetExtension(args[0]).Equals(".json", StringComparison.OrdinalIgnoreCase)))
			{
				string app = Path.GetFileNameWithoutExtension(typeof(Program).Assembly.CodeBase);
				log.Warn("usage: {0} course_url", app);
				log.Warn("usage: {0} course_xml", app);
				log.Warn("usage: {0} course_json [lesson_id,...]", app);
				Environment.ExitCode = 1;
				return;
			}
			string path = args[0];

			try
			{
				CourseProgram course;
				if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
				{
					using (var reader = new FileStream(path, FileMode.Open))
					{
						course = (CourseProgram)CourseProgram.serializer.Value.Deserialize(reader);
					}
					course.Render();
				}
				else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
				{
					var single = new Course(Path.GetFileNameWithoutExtension(path), "http://lingualeo.com/fake");
					using (var reader = new FileStream(path, FileMode.Open))
					{
						var data = new JSONDictionary(reader);
						string[] order = new string[0];
						if (args.Length == 2) order = args[1].Split(',');
						single.ParseJSON(data, order);
					}
					using (var writer = new FileStream(Path.Combine(single.Owner.BasePath, Path.GetFileNameWithoutExtension(path) + ".xml"), FileMode.CreateNew))
					{
						Course.serializer.Value.Serialize(writer, single, CourseProgram.namespaces.Value);
					}
				}
				else
				{
					course = new CourseProgram(path);
					course.Parse();
					using (var writer = new FileStream(Path.Combine(course.BasePath, "Index.xml"), FileMode.CreateNew))
					{
						CourseProgram.serializer.Value.Serialize(writer, course, CourseProgram.namespaces.Value);
					}
				}
			}
			catch (Exception ex)
			{
				for (; ex != null; ex = ex.InnerException)
					log.Error(ex.Message);
			}
			Console.WriteLine("Done. Press any key to close");
			Console.ReadKey();
		}
	}
}
