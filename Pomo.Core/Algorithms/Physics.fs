namespace Pomo.Core.Algorithms

open Microsoft.Xna.Framework

module Physics =

  let applyCollisionSliding (velocity: Vector2) (mtv: Vector2) =
    if mtv <> Vector2.Zero then
      let normal = Vector2.Normalize mtv
      // If moving against the normal (into the wall)
      if Vector2.Dot(velocity, normal) < 0.0f then
        // Project velocity onto the tangent (slide)
        velocity - normal * Vector2.Dot(velocity, normal)
      else
        velocity
    else
      velocity

  let ArrivalThreshold = 2.0f
