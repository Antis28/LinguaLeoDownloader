using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LinguaLeoDownloader
{
	public sealed class PageRender
	{
		private static readonly ILog log = Logger.GetLogger<PageRender>();
		private static Regex reForEach = new Regex(@"foreach\s*\(\s*var\s+(?<item>\w+)\s+in\s+(?<enumerator>\w+)\)\s*{");
		private static Regex reIf = new Regex(@"if\s*\((?<item>\w+)\.(?<flag>\w+)\)\s*{");
		private static Regex reElse = new Regex(@"}\s*else\s*{");
		private IDictionary<string, object> variables = new Dictionary<string, object>();
		private IDictionary<string, IEnumerable> enumerators = new Dictionary<string, IEnumerable>();

		public PageRender(CourseProgram program)
		{
			this.variables.Add("Program", program);
			var fromCoursesGroupByLevel =
				from course in program.Courses
				group course by course.Level into level
				select level;
			enumerators.Add("FromCoursesGroupByLevel", fromCoursesGroupByLevel);
		}

		public PageRender(CourseProgram program, Course couse)
			: this(program)
		{
			this.variables.Add("Course", couse);
		}

		public PageRender(CourseProgram program, Course couse, CourseLesson lesson)
			: this(program, couse)
		{
			this.variables.Add("Lesson", lesson);
		}

		public PageRender(CourseProgram program, Course couse, CourseLesson lesson, CoursePage page)
			: this(program, couse, lesson)
		{
			this.variables.Add("Page", page);
		}

		public void Render(string template, TextWriter writer)
		{
			string page;
			using (Stream stream = typeof(CourseProgram).Assembly.GetManifestResourceStream(typeof(CourseProgram).Namespace + ".Resources." + template))
			using (StreamReader reader = new StreamReader(stream))
			{
				page = reader.ReadToEnd();
			}

			var texts = page.Split(new string[] { "<%" }, StringSplitOptions.None);
			writer.Write(texts[0]);

			Process(writer, texts, 1, texts.Length);
		}

		private string[] SplitCommand(string line)
		{
			var commands = line.Split(new string[] { "%>" }, 2, StringSplitOptions.None);
			if (commands.Length != 2)
				throw new InvalidDataException("Close tag '%>' not found");
			return commands;
		}

		private void SplitCommand(string line, out string command, out string text)
		{
			var commands = SplitCommand(line);
			command = commands[0];
			text = commands[1];
		}

		private void SplitBind(string command, out string item, out string field)
		{
			var bind = command.Substring(1).Trim().Split(new char[] { '.' }, 2);
			item = bind[0];
			field = bind.Length == 2 ? bind[1] : null;
		}

		private object GetValue(object item, string field)
		{
			if (field == null) return item;
			var type = item.GetType();
			var pi = type.GetProperty(field);
			if (pi == null)
				throw new InvalidDataException("Unknown property " + field + " for " + type.Name);
			return pi.GetValue(item, null);
		}

		private void Process(TextWriter writer, string[] texts, int start, int end)
		{
			for (int k = start; k < end; k++)
			{
				string command, text;
				SplitCommand(texts[k], out command, out text);
				if (command.StartsWith("#"))
				{
					string item, field;
					SplitBind(command, out item, out field);
					AddValue(writer, GetValue(variables[item], field));
					writer.Write(text);
					continue;
				}
				command = command.Trim();
				if (command == "}")
				{
					return;
				}
				Match m;
				if ((m = reForEach.Match(command)).Success)
				{
					string name = m.Groups["enumerator"].Value;
					string argument = m.Groups["item"].Value;
					IEnumerable e;
					if (!enumerators.TryGetValue(name, out e))
						e = (IEnumerable)variables[name];
					int end2 = FindNext(texts, k + 1, false);
					foreach (object item in e)
					{
						writer.Write(text);
						variables[argument] = item;
						Process(writer, texts, k + 1, end2);
					}
					k = end2;
				}
				else if ((m = reIf.Match(command)).Success)
				{
					string argument = m.Groups["item"].Value;
					string flag = m.Groups["flag"].Value;
					int end2 = FindNext(texts, k + 1, true);
					int mid2 = end2;
					if (SplitCommand(texts[end2])[0].Trim() != "}")
					{
						end2 = FindNext(texts, mid2 + 1, false);
					}
					if ((bool)GetValue(variables[argument], flag))
					{
						writer.Write(text);
						Process(writer, texts, k + 1, mid2);
					}
					else if (mid2 < end2)
					{
						writer.Write(SplitCommand(texts[mid2])[1]);
						Process(writer, texts, mid2 + 1, end2);
					}
					k = end2;
				}
				else
				{
					throw new InvalidDataException("Only enumeration like 'foreach(var item in enumerator) {' or condition like 'if (item.flag) {' supported");
				}
				writer.Write(SplitCommand(texts[k])[1]);
			}
		}

		private int FindNext(string[] texts, int start, bool ifBlock)
		{
			int level = 0;
			for (int k = start; k < texts.Length; k++)
			{
				var texts2 = texts[k].Split(new string[] { "%>" }, 2, StringSplitOptions.None);
				if (texts2.Length != 2)
					throw new InvalidDataException("Close tag '%>' not found");
				if (texts2[0].StartsWith("#")) continue;
				string text = texts2[0].Trim();
				if (text == "}")
				{
					if (level == 0) return k;
					level--;
				}
				else if (ifBlock && reElse.IsMatch(text))
				{
					if (level == 0) return k;
				}
				else
				{
					level++;
				}
			}
			throw new InvalidDataException("Close brace '<% } %>' not found");
		}

		private void AddValue(TextWriter writer, object value)
		{
			writer.Write(value);
		}
	}
}
