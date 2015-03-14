using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace LinguaLeoDownloader
{
	[Flags]
	public enum LanguageLevel
	{
		Unknown = 0,
		Beginner = 1,
		Intermediate = 2,
		Advanced = 4
	}

	public struct JSONDictionary
	{
		private IDictionary<string, object> dic;

		public JSONDictionary(object value)
		{
			this.dic = value as IDictionary<string, object>;
		}

		public JSONDictionary(Stream file)
		{
			string page;
			using (var reader = new StreamReader(file))
			{
				page = reader.ReadToEnd();
			}
			var js = new JavaScriptSerializer();
			this.dic = js.DeserializeObject(page) as IDictionary<string, object>;
		}

		public string this[string key]
		{
			get
			{
				object value;
				if (!dic.TryGetValue(key, out value))
					return null;
				return value == String.Empty ? null : (string)value;
			}
		}

		public TValue? Get<TValue>(string key) where TValue : struct
		{
			object value;
			if (!dic.TryGetValue(key, out value))
				return null;
			return (TValue?)value;
		}

		public TValue Get<TValue>(string key, TValue defValue) where TValue : struct
		{
			object value;
			if (!dic.TryGetValue(key, out value))
				return defValue;
			return (TValue?)value ?? defValue;
		}

		public int this[string key, int defValue]
		{
			get { return Get(key, defValue); }
		}

		public bool this[string key, bool defValue]
		{
			get { return Get(key, defValue); }
		}

		public JSONDictionary Child(string key)
		{
			return new JSONDictionary(dic[key] as IDictionary<string, object> ?? new Dictionary<string, object>());
		}

		public JSONDictionary Child(int key)
		{
			return Child(key.ToString());
		}

		public JSONDictionary[] Items(string key)
		{
			return ((object[])dic[key]).Select(item => new JSONDictionary((IDictionary<string, object>)item)).ToArray();
		}

		public IEnumerable<JSONDictionary> Values
		{
			get { return dic.Values.Cast<IDictionary<string, object>>().Select(child => new JSONDictionary(child)); }
		}

		public IEnumerable<KeyValuePair<int, JSONDictionary>> Children
		{
			get { return dic.Select(item => new KeyValuePair<int, JSONDictionary>
				(int.Parse(item.Key), new JSONDictionary((IDictionary<string, object>)item.Value)));
			}
		}

		public DateTime this[string key, DateTime defDate]
		{
			get
			{
				if (defDate == DateTime.MinValue)
					return Get(key, 0).FromUNIX();
				else
					return Get(key, defDate.ToUNIX()).FromUNIX();
			}
		}
	}

	public static class ValueExtensions
	{
		private const long EpochUNIX = 62135596800L * TimeSpan.TicksPerSecond;//new DateTime(1970, 1, 1).Ticks;

		public static XmlNode ToXML(this string text)
		{
			if (text == null)
				throw new ArgumentNullException("text");
			XmlDocument doc = new XmlDocument();
			if (text != null && (text.Contains("<") || text.Contains(">") || text.Contains("\n")))
				return doc.CreateCDataSection(text);
			else
				return doc.CreateTextNode(text);
		}

		public static DateTime FromUNIX(this int value)
		{
			return new DateTime(TimeSpan.TicksPerSecond * value + EpochUNIX);
		}

		public static DateTime? FromUNIX(this int? value)
		{
			if (value == null) return null;
			return FromUNIX(value.Value);
		}

		public static int ToUNIX(this DateTime date)
		{
			return (int)((date.Ticks - EpochUNIX) / TimeSpan.TicksPerSecond);
		}
	}

	internal class DownloadManager
	{
		private static readonly ILog log = Logger.GetLogger<CourseProgram>();
		private Uri uri;
		private Lazy<CookieContainer> cookies;

		[DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
		static extern bool InternetGetCookie(string lpszUrlName, string lpszCookieName,
			[Out] StringBuilder lpszCookieData, [MarshalAs(UnmanagedType.U4)] ref int lpdwSize);

		private static CookieContainer LoadCookies(Uri uri)
		{
			var cookies = new CookieContainer();

			var cookieBuilder = new StringBuilder(new string(' ', 256), 256);
			int cookieSize = cookieBuilder.Length;

			if (!InternetGetCookie(uri.AbsoluteUri, null, cookieBuilder, ref cookieSize))
			{
				if (cookieSize == 0)
					return cookies;
				cookieBuilder = new StringBuilder(cookieSize);
				InternetGetCookie(uri.AbsoluteUri, null, cookieBuilder, ref cookieSize);
			}

			cookies.SetCookies(uri, cookieBuilder.ToString().Replace(";", ","));
			return cookies;
		}

		public DownloadManager(Uri uri)
		{
			this.uri = uri;
			this.cookies = new Lazy<CookieContainer>(() => LoadCookies(this.uri));
		}

		public string DownloadPage(bool cookies = true)
		{
			var request = (HttpWebRequest)WebRequest.Create(this.uri);
			if (cookies)
				request.CookieContainer = this.cookies.Value;

			log.Debug("Open " + uri.AbsoluteUri);
			using (var response = (HttpWebResponse)request.GetResponse())
			{
				if (response.StatusCode != HttpStatusCode.OK)
					throw new InvalidOperationException("Response status is " + response.StatusDescription);

				using (var receiveStream = response.GetResponseStream())
				{
					var readStream = response.CharacterSet == null ?
						new StreamReader(receiveStream) :
						new StreamReader(receiveStream, Encoding.GetEncoding(response.CharacterSet));
					return readStream.ReadToEnd();
				}
			}
		}

		public JSONDictionary DownloadCourse()
		{
			string page = DownloadPage(true);
			int start = page.IndexOf("CONFIG.pages.course = ");
			int end = page.IndexOf("}; </script>", start + 1);
			if (start < 0 || end < 0)
				throw new InvalidDataException("Course not found");

			page = page.Substring(start + 22, end - start - 21);
			var js = new JavaScriptSerializer();
			return new JSONDictionary(js.DeserializeObject(page));
		}

		public void DownloadFile(string url, string local, bool cookies = false)
		{
			var uri = new Uri(this.uri, url);
			var request = (HttpWebRequest)WebRequest.Create(uri);
			if (cookies)
				request.CookieContainer = this.cookies.Value;
			log.Debug("Download " + uri.AbsoluteUri);

			using (var response = (HttpWebResponse)request.GetResponse())
			{
				if (response.StatusCode != HttpStatusCode.OK)
					throw new InvalidOperationException("Response status is " + response.StatusDescription);

				using (var fs = new FileStream(local, FileMode.CreateNew))
				{
					using (var receiveStream = response.GetResponseStream())
					{
						receiveStream.CopyTo(fs);
					}
				}
			}
		}
	}
}
