#!/usr/bin/env -S dotnet fsi

#r "nuget: SkiaSharp"

open System
open System.IO
open SkiaSharp

// Define constants
let topLevelPath =
  Path.Combine(__SOURCE_DIRECTORY__, "../../Pomo.DesktopGL/AppIcon.xcassets")

let xcassetsPath = Path.Combine(topLevelPath, "AppIcon.appiconset")

let iconSizes = [
  "icon_16x16.png", 16
  "icon_32x32.png", 32
  "icon_64x64.png", 64
  "icon_128x128.png", 128
  "icon_256x256.png", 256
  "icon_512x512.png", 512
  "icon_1024x1024.png", 1024
]

// Function to create directories
let createDirectories() =
  if Directory.Exists topLevelPath then
    Directory.Delete(topLevelPath, true)
    printfn "Deleted existing directory: %s" topLevelPath

  Directory.CreateDirectory xcassetsPath |> ignore
  printfn "Created directory: %s" xcassetsPath

// Function to resize images
let resizeImage (inputPath: string) (outputPath: string) (size: int) =
  use input = SKBitmap.Decode inputPath

  if isNull input then
    failwithf "Failed to decode image from path: %s" inputPath

  let resized =
    input.Resize(
      new SKImageInfo(size, size),
      SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)
    )

  if isNull resized then
    failwithf "Failed to resize image from path: %s" inputPath

  use image = SKImage.FromBitmap(resized)
  use data = image.Encode(SKEncodedImageFormat.Png, 100)
  use stream = File.OpenWrite(outputPath)
  data.SaveTo stream

// Generate icons
let generateIcons() =
  let inputPath = Path.Combine(__SOURCE_DIRECTORY__, "icon-1024.png")

  iconSizes
  |> List.iter(fun (fileName, size) ->
    let outputPath = Path.Combine(xcassetsPath, fileName)
    resizeImage inputPath outputPath size)

// Generate Contents.json
let generateContentsJson() =
  let contentsJson =
    """{
  "images" : [
    { "filename": "icon_16x16.png", "idiom": "mac", "scale": "1x", "size": "16x16" },
    { "filename": "icon_32x32.png", "idiom": "mac", "scale": "2x", "size": "16x16" },
    { "filename": "icon_32x32.png", "idiom": "mac", "scale": "1x", "size": "32x32" },
    { "filename": "icon_64x64.png", "idiom": "mac", "scale": "2x", "size": "32x32" },
    { "filename": "icon_128x128.png", "idiom": "mac", "scale": "1x", "size": "128x128" },
    { "filename": "icon_256x256.png", "idiom": "mac", "scale": "2x", "size": "128x128" },
    { "filename": "icon_256x256.png", "idiom": "mac", "scale": "1x", "size": "256x256" },
    { "filename": "icon_512x512.png", "idiom": "mac", "scale": "2x", "size": "256x256" },
    { "filename": "icon_512x512.png", "idiom": "mac", "scale": "1x", "size": "512x512" },
    { "filename": "icon_1024x1024.png", "idiom": "mac", "scale": "2x", "size": "512x512" }
  ],
  "info" : { "author" : "xcode", "version" : 1 }
}"""

  let outputPath = Path.Combine(xcassetsPath, "Contents.json")
  File.WriteAllText(outputPath, contentsJson)

// Main execution
printfn "macOS Icon Generation Started!"
createDirectories()
generateIcons()
generateContentsJson()
printfn "macOS Icon Generation Complete!"
