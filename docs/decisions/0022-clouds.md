# 0022. Clouds: a climate map and a shader-drawn sky
- Status: accepted
- Date: 2026-10-22

## Context
The sky was a clean gradient. The developer asked for clouds that look as real as possible, with different density and thickness in different regions.

## Options considered
1. Cloud meshes or particle billboards — many objects, no parallax, hard to light.
2. Ray-marched volumetric clouds — the most realistic, far too slow for the retro, 60 fps target at this scale.
3. Layered cloud slices drawn inside the sky shader from tileable noise, thresholded by a baked world climate map — no objects, no overdraw cost beyond the background pixels, correct perspective and parallax, and regional weather comes from data.

## Decision
Option 3.
- **Climate map** (`Realm.Gen.Weather.CloudClimateGenerator`, `clouds.bin`): two 256 x 256 byte maps over the world, cover and thickness. Cover and thickness come from each biome's `rainfall` property (wetter is cloudier and deeper), the open sea gets middling values, high ground gathers extra cloud (peaks wear hats), the biome shapes are blurred across borders and broken up with low-frequency noise. No new biome property is needed; one may be added later to override a biome.
- **Noise** (`CloudTextureGenerator`): one tileable 256 px RGBA texture, generated from the world seed: shapes, billows (cell noise), fine detail, and horizontal streaks.
- **Sky shader** (in `DayNightCycle`): for each pixel the view ray is intersected with world-space cloud layers above the camera, so clouds sit at real positions and the weather over a distant region is the weather of that region. A low layer of two parallax slices (the upper one only in deep cloud), lit from the sun's side with a sample toward the sun, grey where thick, with a bright rim near the sun; a high layer of thin streaky cloud that drifts faster. Colours come from the same palette as the lights, so clouds turn orange at sunset and dark moonlit grey at night. Cloud opacity grows slowly with density and tops out below 1, so the sun and moon show through gaps and thin edges and glow through thin cloud; deep overcast hides them. Wind drifts them (14 m/s, scaled with the time speed), and the cover swells and fades slowly over the days.
- **Light** (`DayNightCycle.Update`): the cloud overhead at the camera (cover times thickness, smoothed) dims the sun and moon, and greys the ambient light and the fog, so a storm over the mountains feels dark and a clear steppe feels bright.

- **Sun and cloud interaction** (added the same day): crepuscular rays in the sky shader, streaks radiating from the sun that need a broken sky (clear air has nothing to cast them, overcast hides the sun) and show best at low sun, faded where cloud covers them. **Cloud shadows**: a full-screen multiply (`ShadowShader`, a quad with `blend_mul` that rebuilds each pixel's world position from the depth buffer) follows the sun's ray from every visible surface up to the same cloud layers the sky draws and reads their density, so ground, trees and buildings darken, with a cool tint, exactly under the clouds that block the sun. The cloud noise, climate map and density function are one shared GLSL block (`CloudCommon`). The CPU overcast term now only greys ambient light and fog and dims the sun lightly, since the shadows do the rest. Both effects need the Forward+ renderer (depth texture) and are cheap: a few texture reads per pixel.

## Consequences
- Clouds cost a few texture reads per sky pixel; the sky is drawn at the render resolution.
- Cloud shadows multiply the whole lit image (ambient included), so they are a little darker than a true direct-light shadow would be; water and other transparent surfaces are not shadowed. The cloud map is fixed per world (only the bias moves over time). Rain and storms are later steps and would read the same map.
- The look needs tuning by eye: thresholds, shading contrast, altitudes and wind speed are constants in the shader and in `DayNightCycle`.
