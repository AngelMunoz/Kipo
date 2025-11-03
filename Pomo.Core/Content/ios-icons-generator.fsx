#!/usr/bin/env -S dotnet fsi

#r "nuget: SkiaSharp"

open System
open System.IO
open SkiaSharp

// Define constants
let topLevelPath =
  Path.Combine(__SOURCE_DIRECTORY__, "../../Pomo.iOS/AppIcon.xcassets")

let xcassetsPath = Path.Combine(topLevelPath, "AppIcon.appiconset")

let iconSizes = [
  "icon_20x20.png", 20
  "icon_29x29.png", 29
  "icon_40x40.png", 40
  "icon_58x58.png", 58
  "icon_60x60.png", 60
  "icon_76x76.png", 76
  "icon_80x80.png", 80
  "icon_87x87.png", 87
  "icon_120x120.png", 120
  "icon_152x152.png", 152
  "icon_167x167.png", 167
  "icon_180x180.png", 180
  "icon_1024x1024.png", 1024
]

// Function to create directories
let createDirectories() =
  if Directory.Exists topLevelPath then
    Directory.Delete(topLevelPath, true)
    printfn "Deleted existing directory: %s" topLevelPath

  Directory.CreateDirectory(xcassetsPath) |> ignore
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

  use image = SKImage.FromBitmap resized
  use data = image.Encode(SKEncodedImageFormat.Png, 100)
  use stream = File.OpenWrite outputPath
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
    { "filename" : "icon_40x40.png", "idiom" : "iphone", "scale" : "2x", "size" : "20x20" },
    { "filename" : "icon_60x60.png", "idiom" : "iphone", "scale" : "3x", "size" : "20x20" },
    { "filename" : "icon_58x58.png", "idiom" : "iphone", "scale" : "2x", "size" : "29x29" },
    { "filename" : "icon_87x87.png", "idiom" : "iphone", "scale" : "3x", "size" : "29x29" },
    { "filename" : "icon_80x80.png", "idiom" : "iphone", "scale" : "2x", "size" : "40x40" },
    { "filename" : "icon_120x120.png", "idiom" : "iphone", "scale" : "3x", "size" : "40x40" },
    { "filename" : "icon_120x120.png", "idiom" : "iphone", "scale" : "2x", "size" : "60x60" },
    { "filename" : "icon_180x180.png", "idiom" : "iphone", "scale" : "3x", "size" : "60x60" },
    { "filename" : "icon_20x20.png", "idiom" : "ipad", "scale" : "1x", "size" : "20x20" },
    { "filename" : "icon_40x40.png", "idiom" : "ipad", "scale" : "2x", "size" : "20x20" },
    { "filename" : "icon_29x29.png", "idiom" : "ipad", "scale" : "1x", "size" : "29x29" },
    { "filename" : "icon_58x58.png", "idiom" : "ipad", "scale" : "2x", "size" : "29x29" },
    { "filename" : "icon_40x40.png", "idiom" : "ipad", "scale" : "1x", "size" : "40x40" },
    { "filename" : "icon_80x80.png", "idiom" : "ipad", "scale" : "2x", "size" : "40x40" },
    { "filename" : "icon_76x76.png", "idiom" : "ipad", "scale" : "1x", "size" : "76x76" },
    { "filename" : "icon_152x152.png", "idiom" : "ipad", "scale" : "2x", "size" : "76x76" },
    { "filename" : "icon_167x167.png", "idiom" : "ipad", "scale" : "2x", "size" : "83.5x83.5" },
    { "filename" : "icon_1024x1024.png", "idiom" : "ios-marketing", "scale" : "1x", "size" : "1024x1024" }
  ],
  "info" : { "author" : "xcode", "version" : 1 }
}"""

  let outputPath = Path.Combine(xcassetsPath, "Contents.json")
  File.WriteAllText(outputPath, contentsJson)

// Main execution
printfn "iOS Icon Generation Started!"
createDirectories()
generateIcons()
generateContentsJson()
printfn "iOS Icon Generation Complete!"
