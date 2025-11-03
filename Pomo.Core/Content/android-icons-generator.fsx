#!/usr/bin/env -S dotnet fsi

#r "nuget: SkiaSharp"

open System.IO
open SkiaSharp

// Define constants
let resourcesBasePath = $"{__SOURCE_DIRECTORY__}/../../Pomo.Android/Resources"
let drawableResourcesPath = Path.Combine(resourcesBasePath, "drawable-")

let densities = [
  "mdpi", 48
  "hdpi", 72
  "xhdpi", 96
  "xxhdpi", 144
  "xxxhdpi", 192
]

let splashSizes = [
  "mdpi", 470
  "hdpi", 640
  "xhdpi", 960
  "xxhdpi", 1440
  "xxxhdpi", 1920
]

// Function to create directories
let createDirectories() =
  densities
  |> List.iter(fun (density, _) ->
    Directory.CreateDirectory $"{drawableResourcesPath + density}" |> ignore)

// Function to resize images
let resizeImage(inputPath: string, outputPath: string, size: int) =
  let absoluteInputPath = Path.GetFullPath inputPath

  let input = SKBitmap.Decode absoluteInputPath

  if isNull input then
    failwithf "Failed to decode image from path: %s" absoluteInputPath

  let samplingOptions =
    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)

  let resized = input.Resize(new SKImageInfo(size, size), samplingOptions)

  if isNull resized then
    failwithf "Failed to resize image from path: %s" absoluteInputPath

  use image = SKImage.FromBitmap resized
  use data = image.Encode(SKEncodedImageFormat.Png, 100)
  use stream = File.OpenWrite outputPath
  data.SaveTo stream

// Generate icons
let generateIcons() =
  densities
  |> List.iter(fun (density, size) ->
    let outputPath = Path.Combine(drawableResourcesPath + density, "icon.png")
    let iconName = __SOURCE_DIRECTORY__ + "/icon-1024.png"
    resizeImage(iconName, outputPath, size))

// Generate splash screens
let generateSplashScreens() =
  splashSizes
  |> List.iter(fun (density, size) ->
    let outputPath =
      Path.Combine(drawableResourcesPath + density, "splash.png")

    let iconName = __SOURCE_DIRECTORY__ + "/splash.png"
    resizeImage(iconName, outputPath, size))

// Main execution
printfn "Generating Android icons"
createDirectories()
generateIcons()
generateSplashScreens()
printfn "Android Generation Complete!"
