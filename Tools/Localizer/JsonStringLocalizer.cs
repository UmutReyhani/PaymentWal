using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using MongoDB.Driver;
using Newtonsoft.Json;
using PaymentWall;
using PaymentWall.Models;

public class JsonStringLocalizer : IStringLocalizer
{
	private readonly IDistributedCache _cache;
	private readonly JsonSerializer _serializer = new JsonSerializer();
	public JsonStringLocalizer(IDistributedCache cache)
	{
		_cache = cache;
	}
	public LocalizedString this[string name]
	{
		get
		{
			string value = GetString(name);
			return new LocalizedString(name, value ?? name, value == null);
		}
	}
	public LocalizedString this[string name, params object[] arguments]
	{
		get
		{
			var actualValue = this[name];
			return !actualValue.ResourceNotFound
				? new LocalizedString(name, string.Format(actualValue.Value, arguments), false)
				: actualValue;
		}
	}
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
	{
		var lan = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName;
		var db2 = config.createMapper();
		var cltranslationProvider = db2.GetCollection<translationProvider>("translationProvider");
		foreach (var item in cltranslationProvider.AsQueryable().ToList())
		{
			yield return new LocalizedString(item.id, item.translation.ContainsKey(lan) ? item.translation[lan] : item.translation.ContainsKey("en") ? item.translation["en"] : item.id, false);
		}
	}
	private string GetString(string key)
	{
		string cacheKey = $"locale_{Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName}_{key}";
		string cacheValue = _cache.GetString(cacheKey);
		if (!string.IsNullOrEmpty(cacheValue)) return cacheValue;
		string result = GetValueFromDB(key, Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName);
		if (!string.IsNullOrEmpty(result)) _cache.SetString(cacheKey, result);
		return result;
	}

	private string GetValueFromDB(string propertyName, string lan)
	{
		if (propertyName == null) return default;
		if (lan == null) return default;

		var db2 = config.createMapper();
		var cltranslationProvider = db2.GetCollection<translationProvider>("translationProvider");
		var item = cltranslationProvider.AsQueryable().FirstOrDefault(x => x.id == propertyName);
		if (item != null)
		{
			if (item.translation.ContainsKey(lan))
			{
				return item.translation[lan];
			}
			else if (item.translation.ContainsKey("en"))
			{
				return item.translation["en"];
			}
			else { return item.id; }
		}
		return default;
	}
}