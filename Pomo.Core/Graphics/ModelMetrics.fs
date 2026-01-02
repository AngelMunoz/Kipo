namespace Pomo.Core.Graphics

open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Animation
open Pomo.Core.Stores
open Pomo.Core

module ModelMetrics =

  let inline private unionBox (a: BoundingBox) (b: BoundingBox) : BoundingBox =
    BoundingBox(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max))

  let inline private translateBox
    (offset: Vector3)
    (box: BoundingBox)
    : BoundingBox =
    BoundingBox(box.Min + offset, box.Max + offset)

  let private tryComputeAssetBounds(model: Model) : BoundingBox voption =
    let mutable hasAny = false
    let mutable unionBounds = Unchecked.defaultof<BoundingBox>

    for mesh in model.Meshes do
      let s = mesh.BoundingSphere
      let center = Vector3.Transform(s.Center, mesh.ParentBone.Transform)
      let r = s.Radius
      let v = Vector3(r, r, r)
      let box = BoundingBox(center - v, center + v)

      if not hasAny then
        unionBounds <- box
        hasAny <- true
      else
        unionBounds <- unionBox unionBounds box

    if hasAny then ValueSome unionBounds else ValueNone

  let private computeNodeOffset
    (rig: HashMap<string, RigNode>)
    (cache: Dictionary<string, Vector3>)
    (pathBuffer: ResizeArray<struct (string * RigNode)>)
    (nodeName: string)
    (node: RigNode)
    : Vector3 =

    match cache |> Dictionary.tryFindV nodeName with
    | ValueSome offset -> offset
    | ValueNone ->
      pathBuffer.Clear()

      let mutable currentName = nodeName
      let mutable currentNode = node
      let mutable baseOffset = Vector3.Zero
      let mutable doneWalking = false

      while not doneWalking do
        match cache |> Dictionary.tryFindV currentName with
        | ValueSome cachedOffset ->
          baseOffset <- cachedOffset
          doneWalking <- true
        | ValueNone ->
          pathBuffer.Add struct (currentName, currentNode)

          match currentNode.Parent with
          | ValueNone ->
            baseOffset <- Vector3.Zero
            doneWalking <- true
          | ValueSome parentName ->
            match rig |> HashMap.tryFindV parentName with
            | ValueSome parentNode ->
              currentName <- parentName
              currentNode <- parentNode
            | ValueNone ->
              baseOffset <- Vector3.Zero
              doneWalking <- true

      let mutable acc = baseOffset

      for i = pathBuffer.Count - 1 downto 0 do
        let struct (name, n) = pathBuffer.[i]
        acc <- acc + n.Offset
        cache[name] <- acc

      acc

  let createPickBoundsResolver
    (content: ContentManager)
    (modelStore: ModelStore)
    : string -> BoundingBox voption =

    let assetBoundsCache = Dictionary<string, BoundingBox>()
    let configBoundsCache = Dictionary<string, BoundingBox>()

    let getAssetBounds(assetPath: string) : BoundingBox voption =
      assetBoundsCache
      |> Dictionary.tryFindV assetPath
      |> ValueOption.orElseWith(fun () ->
        try
          let model = content.Load<Model>(assetPath)

          tryComputeAssetBounds model
          |> ValueOption.map(fun bounds ->
            assetBoundsCache[assetPath] <- bounds
            bounds)
        with _ ->
          ValueNone)

    fun configId ->
      configBoundsCache
      |> Dictionary.tryFindV configId
      |> ValueOption.orElseWith(fun () ->
        modelStore.tryFind configId
        |> ValueOption.bind(fun config ->
          match config.PickBoundsOverride with
          | ValueSome overrideBounds ->
            configBoundsCache[configId] <- overrideBounds
            ValueSome overrideBounds
          | ValueNone ->
            let offsetsCache = Dictionary<string, Vector3>(16)
            let offsetsPath = ResizeArray<struct (string * RigNode)>(16)

            let mutable hasAny = false
            let mutable unionBounds = Unchecked.defaultof<BoundingBox>

            for nodeName, node in config.Rig do
              let nodeOffset =
                computeNodeOffset
                  config.Rig
                  offsetsCache
                  offsetsPath
                  nodeName
                  node

              match getAssetBounds node.ModelAsset with
              | ValueSome assetBounds ->
                let bounds = translateBox nodeOffset assetBounds

                if not hasAny then
                  unionBounds <- bounds
                  hasAny <- true
                else
                  unionBounds <- unionBox unionBounds bounds
              | ValueNone -> ()

            if hasAny then
              configBoundsCache[configId] <- unionBounds
              ValueSome unionBounds
            else
              ValueNone))
