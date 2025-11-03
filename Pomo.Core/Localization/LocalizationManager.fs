namespace Pomo.Core.Localization

open System
open System.Collections.Generic
open System.Globalization
open System.Reflection
open System.Resources
open System.Threading

/// <summary>
/// Manages localization settings for the game, including retrieving supported cultures and setting the current culture for localization.
/// </summary>
module LocalizationManager =

  /// <summary>
  /// the culture code we default to
  /// </summary>
  [<Literal>]
  let DefaultCultureCode = "en-EN"


  /// <summary>
  /// Retrieves a list of supported cultures based on available language resources in the game.
  /// This method checks the current culture settings and the satellite assemblies for available localized resources.
  /// </summary>
  /// <returns>A list of <see cref="CultureInfo"/> objects representing the cultures supported by the game.</returns>
  /// <remarks>
  /// This method iterates through all specific cultures defined in the satellite assemblies and attempts to load the corresponding resource set.
  /// If a resource set is found for a particular culture, that culture is added to the list of supported cultures. The invariant culture
  /// is always included in the returned list as it represents the default (non-localized) resources.
  /// </remarks>
  let GetSupportedCultures() =
    let supportedCultures = ResizeArray()
    let ass = Assembly.GetExecutingAssembly()

    let resourceManager =
      ResourceManager("Pomo.Core.Localization.Resources", ass)

    let cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures)

    for culture in cultures do
      try
        let resourceSet = resourceManager.GetResourceSet(culture, true, false)

        if resourceSet <> null then
          supportedCultures.Add(culture)
      with :? MissingManifestResourceException ->
        ()

    supportedCultures.Add(CultureInfo.InvariantCulture)
    supportedCultures

  /// <summary>
  /// Sets the current culture of the game based on the specified culture code.
  /// This method updates both the current culture and UI culture for the current thread.
  /// </summary>
  /// <param name="cultureCode">The culture code (e.g., "en-US", "fr-FR") to set for the game.</param>
  /// <remarks>
  /// This method modifies the <see cref="Thread.CurrentThread.CurrentCulture"/> and <see cref="Thread.CurrentThread.CurrentUICulture"/> properties,
  /// which affect how dates, numbers, and other culture-specific values are formatted, as well as how localized resources are loaded.
  /// </remarks>
  let SetCulture(cultureCode: string) =
    let culture = CultureInfo cultureCode
    Thread.CurrentThread.CurrentCulture <- culture
    Thread.CurrentThread.CurrentUICulture <- culture
