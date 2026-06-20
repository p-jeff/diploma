# Experience — Idea Backlog

Scratchpad for ideas to discuss and maybe implement. Add freely; we go over them together whenever it fits.
Status: `parked` (not planned) · `discuss` (bring up next session) · `planned` · `done` · `dropped`

### Planned / ToDos

### Plancement Algo takes in account all plants — `done`
The Spread / Placement algorithm should take all already grown plants into account while placing new plants, with specific margins for different plants and sizes, to fully Auto-grow the garden.
~~Reliant on more specific colliders for each gaussian~~ — unblocked once the final collider meshes were wired. Implemented as the `GardenPlacer` engine (shared no-overlap registry, true mesh-shape spread, edge inset, user/chair keep-outs, and behind-anchor framing). See `GardenPlacement.md`.


## New ideas

<!-- Template — copy below:

### Idea title — `discuss`
One or two sentences: what it is, where in the flow it happens.
- Notes / constraints / assets needed:
-->
### End poem from user choices — discuss
Compose a poem at the end of the experience out of the user's 8 liked species (and possibly which instances they grew), displayed after the flourish.
- Data is already available: `ExperienceManager` knows the liked plants in order; each `Plant` tracks its grown instances (`m_grown`).
- Could be pre-written per-species lines stitched together, or generated.

### 180° Environments — `done`
Adding 180° digital paintings of certain locations / vibes when selecting certain poems / plants / instances.  The idea being, when selecting the instance for lavender about the arabian step, you are actively located in the arabian step for example, via a painting (done by a friend) that is covering most if not all your viewport, almost like you could step inside.

### Photo Printer — `discuss`
If the End Poem is implemented, print one plant and that poem using a postcard printer as a takeaway item for the person viewing the experience at an exhibition.

### Window into the Digital — `discuss`
Have a setup that allows for either a second screen connected to the same computer running the VR experience, or a second computer logging in via multiplayer on local network, that has certain render filters and can act as a window into the digital world for external viewers. Possibly could also be done using a projector?

### Real-world occlusion for plants — `discuss`
Let real-world geometry (hands, furniture, room) occlude the gaussian-splat plants so they feel grounded in passthrough. Currently the project has no occlusion (no environment depth, no scene/room mesh, no Meta occlusion building block — only old refs in `_Projects/Old/ScanAlignment`).

**Key lever:** `Gsplat_Modified.shader` is `ZWrite Off` / Queue `Transparent` but does NOT override `ZTest`, so it uses default `ZTest LEqual` — the splats already depth-test against the buffer. Anything that writes depth *before* the plants render will occlude them for free. The old `_Projects/Old/ScanAlignment/3.3/DepthOccluder.shader` (`ColorMask 0`, `ZWrite On`, `Queue Geometry-1`) is exactly the trick.

**Passthrough synergy:** since `ForceOpaqueAlpha` was removed, the compositor shows passthrough wherever the app doesn't draw — so "occluded" = just don't draw that fragment → the real object shows through automatically. No extra compositing needed.

**Tier 1 — occluder geometry (low effort, coarse):**
- Hand mesh occluder → hands pass in front of plants. Highest "feels real" payoff, nearly free, shader pattern already exists.
- Quest Scene / MRUK room mesh + furniture volumes as invisible depth-only occluders.
- Downsides: hard binary edges (splats straddling the boundary pop); only as accurate as the scene capture (no occlusion from uncaptured objects).

**Tier 2 — per-pixel environment depth (general, more work):**
- Quest 3 Depth API: sample `_EnvironmentDepthTexture` in the gsplat fragment shader, reproject, compare vs splat depth, discard / soft-blend alpha.
- General occlusion from anything the depth sensor sees (no need to model it); supports soft edges.
- Cost: texture sample + reprojection per fragment in an already fill-heavy transparent shader — profile against the ~6ms/90Hz headroom. Quest 3-only.

**Plant ↔ virtual occlusion (harder, lower payoff):** opaque virtual geo occluding plants works (opaque writes depth first); plants occluding opaque geo behind them does NOT (splats never write depth — classic transparent limitation, would need a splat-depth pre-pass); plant-on-plant is already handled by the radix sort.

**Recommendation:** start with Tier 1 hand occluders; add room/furniture occluders if doing scene capture; only reach for Tier 2 if occlusion from arbitrary uncaptured objects is needed and the frame budget allows.

**Verify first:** gsplat renders via `Graphics.RenderMeshPrimitives` — confirm it draws into the camera's shared depth buffer and that occluders land in an earlier queue. The whole approach rests on this.

## Tuning wishes from headset testing

<!-- Things that felt wrong in headset that aren't bugs — placement, timing, scale, comfort. -->
