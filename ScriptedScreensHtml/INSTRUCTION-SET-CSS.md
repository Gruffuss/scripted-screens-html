Rules and columns: see INSTRUCTION-SET.md.

## 2. CSS

### 2a. Properties

Sourced from the W3C canonical machine-readable list (`https://www.w3.org/Style/CSS/all-properties.en.json`), fetched in narrow exact-match chunks and cross-checked (see Sources). Each JSON entry carries `property`, `url`, `status`, `title` (module/spec). A property can be defined by more than one module; where the JSON gives more than one distinct module family for a property, it is listed once under each family (deliberate cross-listing, not duplication). Where the JSON gives more than one *level* of the same module family (e.g. Box Sizing Level 3 vs Level 4), the property is listed once under that family's heading using the more current/highest level's status. The status tag is whatever the JSON's `status` field says for the entry used (REC, CR, CRD, FPWD, WD, ED, etc.), preferring a TR-published status over a same-module ED-only entry when both exist.

#### CSS 2.1 (Legacy — no modern module in this JSON)
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| azimuth (REC) | | | | |
| border-spacing (REC) | | | | |
| elevation (REC) | | | | |
| voice-balance (REC) | | | | |
| voice-duration (REC) | | | | |
| voice-family (REC) | | | | |
| voice-pitch (REC) | | | | |
| voice-pitch-range (REC) | | | | |
| voice-range (REC) | | | | |
| voice-rate (REC) | | | | |
| voice-stress (REC) | | | | |
| voice-volume (REC) | | | | |
| volume (REC) | | | | |

#### CSS Cascading and Inheritance
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| all (CR) | | | | |

#### CSS Anchor Positioning
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| align-self (WD) | | | | |
| anchor-name (WD) | | | | |
| anchor-scope (WD) | | | | |
| bottom (WD) | | | | |
| justify-self (WD) | | | | |
| position (WD) | | | | |

#### CSS Animations
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| animation (WD) | | | | |
| animation-composition (WD) | | | | |
| animation-delay (WD) | | | | |
| animation-delay-end (ED) | | | | |
| animation-delay-start (ED) | | | | |
| animation-direction (WD) | | | | |
| animation-duration (WD) | | | | |
| animation-fill-mode (WD) | | | | |
| animation-iteration-count (WD) | | | | |
| animation-name (WD) | | | | |
| animation-play-state (WD) | | | | |
| animation-timeline (WD) | | | | |
| animation-timing-function (WD) | | | | |

#### Scroll-driven Animations
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| animation-range (WD) | | | | |
| animation-range-end (WD) | | | | |
| animation-range-start (WD) | | | | |

#### CSS Basic User Interface
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| accent-color (WD) | | | | |
| appearance (WD) | | | | |
| caret-color (WD) | | | | |
| caret-shape (WD) | | | | |
| cursor (WD) | | | | |
| ime-mode (ED, Level 3 only) | | | | |
| input-security (WD) | | | | |
| interaction (WD) | | | | |
| outline (WD) | | | | |
| outline-color (WD) | | | | |
| outline-offset (WD) | | | | |
| outline-style (WD) | | | | |
| outline-width (WD) | | | | |
| pointer-events (WD) | | | | |
| resize (WD) | | | | |
| user-select (WD) | | | | |

#### CSS Box Alignment
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| align-content (WD) | | | | |
| align-items (WD) | | | | |
| align-self (WD) | | | | |
| column-gap (WD) | | | | |
| gap (WD) | | | | |
| justify-content (WD) | | | | |
| justify-items (WD) | | | | |
| justify-self (WD) | | | | |
| row-gap (WD) | | | | |

#### CSS Flexible Box Layout (Flexbox)
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| align-content (CRD) | | | | |
| align-items (CRD) | | | | |
| flex (CRD) | | | | |
| flex-basis (CRD) | | | | |
| flex-direction (CRD) | | | | |
| flex-flow (CRD) | | | | |
| flex-grow (CRD) | | | | |
| flex-shrink (CRD) | | | | |
| flex-wrap (CRD) | | | | |
| gap (CRD) | ✅ | laid out once at the compile, as positions in the scene: the layout's own gap, between the items shown only, so rows a state shows or hides (a list's rows, a row's shapes) keep it between those shown and none after the last, as margins would; a block's children get none | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a flex gap between list rows that have two shapes, the last row's shape changing: the gap is between the rows shown, as margins would be", plain-flex.lua; "plain gap: between the flex items shown and none on a block's children" | |
| justify-content (CRD) | | | | |
| order (CRD) | | | | |

#### CSS Grid Layout
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| gap (WD) | | | | |
| grid (WD) | | | | |
| grid-area (WD) | | | | |
| grid-auto-columns (WD) | | | | |
| grid-auto-flow (WD) | | | | |
| grid-auto-rows (WD) | | | | |
| grid-column (WD) | | | | |
| grid-column-end (WD) | | | | |
| grid-column-start (WD) | | | | |
| grid-row (WD) | | | | |
| grid-row-end (WD) | | | | |
| grid-row-start (WD) | | | | |
| grid-template (WD) | | | | |
| grid-template-areas (WD) | | | | |
| grid-template-columns (WD) | | | | |
| grid-template-rows (WD) | | | | |
| row-gap (CRD) | | | | |

#### CSS Box Sizing
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| aspect-ratio (WD) | | | | |
| block-size (WD) | | | | |
| box-sizing (WD) | | | | |
| height (WD) | | | | |
| inline-size (WD) | | | | |
| inline-sizing (WD) | | | | |
| max-block-size (WD) | | | | |
| max-height (WD) | | | | |
| max-inline-size (WD) | | | | |
| max-width (WD) | | | | |
| min-block-size (WD) | | | | |
| min-height (WD) | | | | |
| min-inline-size (WD) | | | | |
| min-width (WD) | | | | |
| width (WD) | | | | |

#### CSS Box Model (Margin / Padding)
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| margin (WD) | | | | |
| margin-block (WD) | | | | |
| margin-block-end (WD) | | | | |
| margin-block-start (WD) | | | | |
| margin-bottom (WD) | | | | |
| margin-inline (WD) | | | | |
| margin-inline-end (WD) | | | | |
| margin-inline-start (WD) | | | | |
| margin-left (WD) | | | | |
| margin-right (WD) | | | | |
| margin-top (WD) | | | | |
| padding (WD) | | | | |
| padding-block (WD) | | | | |
| padding-block-end (WD) | | | | |
| padding-block-start (WD) | | | | |
| padding-bottom (WD) | | | | |
| padding-inline (WD) | | | | |
| padding-inline-end (WD) | | | | |
| padding-inline-start (WD) | | | | |
| padding-left (WD) | | | | |
| padding-right (WD) | | | | |
| padding-top (WD) | | | | |

#### CSS Backgrounds and Borders
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| background (FPWD) | | | | |
| background-attachment (FPWD) | | | | |
| background-clip (FPWD) | | | | |
| background-color (FPWD) | | | | |
| background-image (FPWD) | | | | |
| background-origin (FPWD) | | | | |
| background-position (FPWD) | | | | |
| background-position-block (FPWD) | | | | |
| background-position-inline (FPWD) | | | | |
| background-position-x (FPWD) | | | | |
| background-position-y (FPWD) | | | | |
| background-repeat (FPWD) | | | | |
| background-repeat-block (FPWD) | | | | |
| background-repeat-inline (FPWD) | | | | |
| background-repeat-x (FPWD) | | | | |
| background-repeat-y (FPWD) | | | | |
| background-size (FPWD) | | | | |
| background-tbd (FPWD) | | | | |
| border (WD) | | | | |
| border-block (WD) | | | | |
| border-block-clip (WD) | | | | |
| border-block-color (WD) | | | | |
| border-block-end (WD) | | | | |
| border-block-end-clip (WD) | | | | |
| border-block-end-color (WD) | | | | |
| border-block-end-radius (WD) | | | | |
| border-block-end-style (WD) | | | | |
| border-block-end-width (WD) | | | | |
| border-block-start (WD) | | | | |
| border-block-start-clip (WD) | | | | |
| border-block-start-color (WD) | | | | |
| border-block-start-radius (WD) | | | | |
| border-block-start-style (WD) | | | | |
| border-block-start-width (WD) | | | | |
| border-block-style (WD) | | | | |
| border-block-width (WD) | | | | |
| border-bottom (WD) | | | | |
| border-bottom-clip (WD) | | | | |
| border-bottom-color (WD) | | | | |
| border-bottom-left-radius (WD) | | | | |
| border-bottom-radius (WD) | | | | |
| border-bottom-right-radius (WD) | | | | |
| border-bottom-style (WD) | | | | |
| border-bottom-width (WD) | | | | |
| border-clip (WD) | | | | |
| border-color (WD) | | | | |
| border-end-end-radius (WD) | | | | |
| border-end-start-radius (WD) | | | | |
| border-image (WD) | | | | |
| border-image-outset (WD) | | | | |
| border-image-repeat (WD) | | | | |
| border-image-slice (WD) | | | | |
| border-image-source (WD) | | | | |
| border-image-width (WD) | | | | |
| border-inline (WD) | | | | |
| border-inline-clip (WD) | | | | |
| border-inline-color (WD) | | | | |
| border-inline-end (WD) | | | | |
| border-inline-end-clip (WD) | | | | |
| border-inline-end-color (WD) | | | | |
| border-inline-end-radius (WD) | | | | |
| border-inline-end-style (WD) | | | | |
| border-inline-end-width (WD) | | | | |
| border-inline-start (WD) | | | | |
| border-inline-start-clip (WD) | | | | |
| border-inline-start-color (WD) | | | | |
| border-inline-start-radius (WD) | | | | |
| border-inline-start-style (WD) | | | | |
| border-inline-start-width (WD) | | | | |
| border-inline-style (WD) | | | | |
| border-inline-width (WD) | | | | |
| border-left (WD) | | | | |
| border-left-clip (WD) | | | | |
| border-left-color (WD) | | | | |
| border-left-radius (WD) | | | | |
| border-left-style (WD) | | | | |
| border-left-width (WD) | | | | |
| border-limit (WD) | | | | |
| border-radius (WD) | | | | |
| border-right (WD) | | | | |
| border-right-clip (WD) | | | | |
| border-right-color (WD) | | | | |
| border-right-radius (WD) | | | | |
| border-right-style (WD) | | | | |
| border-right-width (WD) | | | | |
| border-style (WD) | | | | |
| border-top (WD) | | | | |
| border-top-clip (WD) | | | | |
| border-top-color (WD) | | | | |
| border-top-left-radius (WD) | | | | |
| border-top-radius (WD) | | | | |
| border-top-right-radius (WD) | | | | |
| border-top-style (WD) | | | | |
| border-top-width (WD) | | | | |
| border-width (WD) | | | | |
| box-shadow (CRD) | | | | |
| box-decoration-break (WD, CSS Fragmentation) | | | | |
| padding (Backgrounds&Borders alias, WD — see Box Model) | | | | |

#### CSS Shadows
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| box-shadow (WD, Level 4) | | | | |

#### CSS Round Display
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| border-boundary (WD) | | | | |

#### CSS Table
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| border-collapse (WD) | | | | |
| caption-side (WD) | | | | |
| empty-cells (WD) | | | | |
| table-layout (WD) | | | | |

#### CSS Color
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| color (REC, Level 4) | | | | |
| opacity (REC, Level 3) | | | | |

#### CSS Color Adjustment
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| forced-color-adjust (CRD) | | | | |
| print-color-adjust (CRD) | | | | |

#### Compositing and Blending
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| background-blend-mode (CRD) | | | | |
| isolation (CRD) | | | | |
| mix-blend-mode (CRD) | | | | |

#### CSS Fonts
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| font (CR) | | | | |
| font-family (CR) | | | | |
| font-feature-settings (CR) | | | | |
| font-kerning (CR) | | | | |
| font-language-override (CR) | | | | |
| font-optical-sizing (CR) | | | | |
| font-palette (CR) | | | | |
| font-size (CR) | | | | |
| font-size-adjust (CR) | | | | |
| font-smooth (CR) | | | | |
| font-stretch (CR) | | | | |
| font-style (CR) | | | | |
| font-synthesis (CR) | | | | |
| font-variant (REC, Level 3) | | | | |
| font-variant-alternates (REC, Level 3) | | | | |
| font-variant-caps (REC, Level 3) | | | | |
| font-variant-east-asian (REC, Level 3) | | | | |
| font-variant-ligatures (REC, Level 3) | | | | |
| font-variant-numeric (REC, Level 3) | | | | |
| font-weight (REC, Level 3) | | | | |
| unicode-range (WD) | | | | |

#### CSS Text
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| hanging-punctuation (WD) | | | | |
| hyphenate-character (WD) | | | | |
| hyphenate-limit-chars (WD) | | | | |
| hyphenate-limit-last (WD) | | | | |
| hyphenate-limit-zone (WD) | | | | |
| hyphens (WD) | | | | |
| letter-spacing (CRD, Level 3) | | | | |
| line-break (CRD) | | | | |
| overflow-wrap (WD) | | | | |
| tab-size (WD) | | | | |
| text-align (WD, Level 4) | | | | |
| text-align-all (WD) | | | | |
| text-align-last (WD, Level 4) | | | | |
| text-indent (CRD, Level 3) | | | | |
| text-justify (CRD) | | | | |
| text-spacing (WD) | | | | |
| text-spacing-trim (WD) | | | | |
| text-transform (CRD, Level 3) | | | | |
| unicode-bidi (WD) | | | | |
| white-space (CRD, Level 3) | | | | |
| white-space-collapse (WD) | | | | |
| white-space-trim (WD) | | | | |
| word-break (CRD, Level 3) | | | | |
| word-spacing (CRD, Level 3) | | | | |
| word-wrap (CRD, Level 3) | | | | |

#### CSS Text Decoration
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| text-decoration (WD, Level 3) | | | | |
| text-decoration-color (WD) | | | | |
| text-decoration-line (WD) | | | | |
| text-decoration-skip (WD) | | | | |
| text-decoration-skip-ink (WD) | | | | |
| text-decoration-style (WD) | | | | |
| text-decoration-thickness (WD) | | | | |
| text-emphasis (CRD) | | | | |
| text-emphasis-color (CRD) | | | | |
| text-emphasis-position (CRD) | | | | |
| text-emphasis-style (CRD) | | | | |
| text-shadow (CRD) | | | | |
| text-underline-offset (CRD) | | | | |
| text-underline-position (CRD) | | | | |

#### CSS Text Size Adjust
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| text-size-adjust (WD) | | | | |

#### CSS Inline Layout
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| alignment-baseline (WD) | | | | |
| baseline-shift (WD) | | | | |
| baseline-source (WD) | | | | |
| initial-letter (WD) | | | | |
| initial-letter-align (WD) | | | | |
| line-height (WD) | | | | |
| text-anchor (WD) | | | | |
| vertical-align (WD) | | | | |

#### CSS Rhythmic Sizing
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| block-step (WD) | | | | |
| block-step-align (WD) | | | | |
| block-step-insert (WD) | | | | |
| block-step-round (WD) | | | | |
| block-step-size (WD) | | | | |

#### CSS Generated Content
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| bookmark-label (WD) | | | | |
| bookmark-level (WD) | | | | |
| bookmark-state (WD) | | | | |
| content (WD) | | | | |
| counter-increment (WD) | | | | |
| counter-reset (WD) | | | | |
| counter-set (WD) | | | | |
| marker (WD) | | | | |
| quotes (WD) | | | | |

#### CSS Marker Properties
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| marker-end (WD) | | | | |
| marker-mid (WD) | | | | |
| marker-start (WD) | | | | |

#### CSS Masking
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| clip (CRD) | | | | |
| clip-path (CRD) | | | | |
| mask (CRD) | | | | |
| mask-clip (CRD) | | | | |
| mask-composite (CRD) | | | | |
| mask-image (CRD) | | | | |
| mask-mode (CRD) | | | | |
| mask-origin (CRD) | | | | |
| mask-position (CRD) | | | | |
| mask-repeat (CRD) | | | | |
| mask-size (CRD) | | | | |
| mask-type (CRD) | | | | |

#### CSS Multi-column Layout
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| column-count (WD) | | | | |
| column-fill (WD) | | | | |
| column-rule (WD) | | | | |
| column-rule-color (WD) | | | | |
| column-rule-style (WD) | | | | |
| column-rule-width (WD) | | | | |
| column-span (WD) | | | | |
| column-width (WD) | | | | |
| columns (WD) | | | | |

#### CSS Containment
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| contain (REC, Level 1; also Level 2/3 WD) | | | | |
| content-visibility (WD) | | | | |

#### CSS Custom Properties (Variables)
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| --* (CR) | | | | |

#### CSS Display
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| clear (WD) | | | | |
| display (WD, Level 4) | | | | |
| display: flex / grid — its items blockified (Display §2.7) | ✅ | each child of a flex or grid container is an item with a box of its own, never folded into its parent's text: a span in one keeps its own shapes, so a value only known at run time may sit in its attributes (the program's attribute table) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "the children of a flex or grid container are items with boxes of their own, never text: spans with no id or class in a grid row and in a display:flex span carry attributes only known at run time, read back with getAttribute", plain-flex.lua | |

#### Filter Effects
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| filter (WD) | | | | |
| lighting-color (WD) | | | | |

#### CSS Fragmentation
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| box-decoration-break (WD) | | | | |
| break-after (WD) | | | | |
| break-before (WD) | | | | |
| break-inside (WD) | | | | |
| orphans (WD) | | | | |
| page-break-after (WD) | | | | |
| page-break-before (WD) | | | | |
| page-break-inside (WD) | | | | |
| widows (WD, Level 4) | | | | |

#### CSS Paged Media
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| page (WD) | | | | |

#### CSS Logical Properties and Values
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| float (WD) | | | | |
| inset (WD) | | | | |
| inset-block (WD) | | | | |
| inset-block-end (WD) | | | | |
| inset-block-start (WD) | | | | |
| inset-inline (WD) | | | | |
| inset-inline-end (WD) | | | | |
| inset-inline-start (WD) | | | | |
| writing-mode (WD, Level 1) | | | | |

#### CSS Images
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| image-orientation (CRD) | | | | |
| image-rendering (CRD) | | | | |
| image-resolution (CRD) | | | | |
| object-fit (CRD) | | | | |
| object-position (CRD) | | | | |

#### CSS Overflow
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| -webkit-line-clamp (WD) | | | | |
| block-ellipsis (WD) | | | | |
| overflow (WD, Level 4) | | | | |
| overflow-block (WD) | | | | |
| overflow-clip-margin (WD) | | | | |
| overflow-inline (WD) | | | | |
| scrollbar-gutter (WD) | | | | |
| text-overflow (WD, Level 4) | | | | |

#### CSS Overscroll Behavior
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| overscroll-behavior (WD) | | | | |
| overscroll-behavior-block (WD) | | | | |
| overscroll-behavior-inline (WD) | | | | |
| overscroll-behavior-x (WD) | | | | |
| overscroll-behavior-y (WD) | | | | |

#### CSS Scroll Snap
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| overflow-anchor (WD, Level 2) | | | | |
| scroll-margin (WD) | | | | |
| scroll-margin-block (WD) | | | | |
| scroll-margin-block-end (WD) | | | | |
| scroll-margin-block-start (WD) | | | | |
| scroll-margin-bottom (WD) | | | | |
| scroll-margin-inline (WD) | | | | |
| scroll-margin-inline-end (WD) | | | | |
| scroll-margin-inline-start (WD) | | | | |
| scroll-margin-left (WD) | | | | |
| scroll-margin-right (WD) | | | | |
| scroll-margin-top (WD) | | | | |
| scroll-padding (WD) | | | | |
| scroll-padding-block (WD) | | | | |
| scroll-padding-block-end (WD) | | | | |
| scroll-padding-block-start (WD) | | | | |
| scroll-padding-bottom (WD) | | | | |
| scroll-padding-inline (WD) | | | | |
| scroll-padding-inline-end (WD) | | | | |
| scroll-padding-inline-start (WD) | | | | |
| scroll-padding-left (WD) | | | | |
| scroll-padding-right (WD) | | | | |
| scroll-padding-top (WD) | | | | |
| scroll-snap-align (WD) | | | | |
| scroll-snap-stop (WD) | | | | |
| scroll-snap-type (WD) | | | | |

#### CSSOM View
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| scroll-behavior (WD) | | | | |

#### CSS Scrollbars Styling
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| scrollbar-color (WD) | | | | |
| scrollbar-width (WD) | | | | |

#### CSS Shapes
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| shape-image-threshold (CR) | | | | |
| shape-margin (CR) | | | | |
| shape-outside (CR) | | | | |

#### CSS Lists and Counters
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| list-style (WD) | | | | |
| list-style-image (WD) | | | | |
| list-style-position (WD) | | | | |
| list-style-type (WD) | | | | |

#### Motion Path
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| offset (WD) | | | | |
| offset-anchor (WD) | | | | |
| offset-distance (WD) | | | | |
| offset-path (WD) | | | | |
| offset-position (WD) | | | | |
| offset-rotate (WD) | | | | |

#### CSS Positioned Layout
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| inset (WD, cross-listed from Logical Properties) | | | | |
| left (WD) | | | | |
| position (WD, cross-listed from Anchor Positioning) | | | | |
| right (WD) | | | | |
| top (WD) | | | | |
| z-index (CRD) | | | | |

#### CSS 3D Transforms / CSS Transforms
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| backface-visibility (WD) | | | | |
| perspective (WD) | | | | |
| perspective-origin (WD) | | | | |
| rotate (WD) | | | | |
| transform (WD) | | | | |
| transform-box (WD) | | | | |
| transform-origin (WD) | | | | |
| transform-style (WD) | | | | |
| translate (WD) | | | | |

#### CSS Transitions
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| transition (WD) | ✅ | the vector mod's `ease` entry per slot for numbers; colours snap (the vector mod glides numbers only) | PlainTranslatorTests: 09-transition; in game 2026-09-24 | |
| transition-delay (WD) | | | | |
| transition-duration (WD) | | | | |
| transition-property (WD) | | | | |
| transition-timing-function (WD) | | | | |

#### CSS Will Change
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| will-change (CR) | | | | |

#### CSS Writing Modes
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| text-combine-upright (CR, Level 4) | | | | |
| text-orientation (CRD, Level 3) | | | | |
| writing-mode (WD, Level 4) | | | | |

#### CSS Ruby Annotation Layout
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| ruby-align (WD) | | | | |
| ruby-position (WD) | | | | |

#### SVG (paint-order only, via svg2)
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| paint-order (CR) | | | | |

### 2b. Selectors

#### Combinators

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Descendant combinator (` `) — `A B` | | | | |
| Child combinator (`>`) — `A > B` | | | | |
| Next-sibling combinator (`+`) — `A + B` | | | | |
| Subsequent-sibling combinator (`~`) — `A ~ B` | | | | |
| Column combinator (`\|\|`) — `A \|\| B` (Selectors L4 marks this "at risk"; no browser ships it) | | | | |
| Namespace separator (`\|`) — `ns\|E` | | | | |
| Selector list (`,`) — `A, B` | | | | |

#### Basic and attribute selectors

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Type selector — `E` | | | | |
| Universal selector — `*` | | | | |
| Class selector — `.class` | | | | |
| ID selector — `#id` | | | | |
| `[attr]` (attribute presence) | | | | |
| `[attr=value]` (exact value) | | | | |
| `[attr~=value]` (whitespace-separated word match) | | | | |
| `[attr\|=value]` (exact or hyphen-prefixed) | | | | |
| `[attr^=value]` (prefix match) | | | | |
| `[attr$=value]` (suffix match) | | | | |
| `[attr*=value]` (substring match) | | | | |
| Case-insensitivity flag `i` — `[attr=value i]` | | | | |
| Case-sensitivity flag `s` — `[attr=value s]` | | | | |

#### Pseudo-classes

Source: MDN's Pseudo-classes index page, current live categorisation (Elemental, Display state, Input, Linguistic, Location, Resource state, Time-dimensional, Tree-structural, Shadow-structural, User action, Functional, Custom state, Page (print), View transition), plus its non-standard/experimental section.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `:defined` | | | | |
| `:root` | | | | |
| `:empty` | | | | |
| `:nth-child()` | | | | |
| `:nth-last-child()` | | | | |
| `:first-child` | | | | |
| `:last-child` | | | | |
| `:only-child` | | | | |
| `:nth-of-type()` | | | | |
| `:nth-last-of-type()` | | | | |
| `:first-of-type` | | | | |
| `:last-of-type` | | | | |
| `:only-of-type` | | | | |
| `:heading()` | | | | |
| `:open` | | | | |
| `:popover-open` | | | | |
| `:modal` | | | | |
| `:fullscreen` | | | | |
| `:picture-in-picture` | | | | |
| `:xr-overlay` | | | | |
| `:enabled` | | | | |
| `:disabled` | | | | |
| `:read-only` | | | | |
| `:read-write` | | | | |
| `:placeholder-shown` | | | | |
| `:autofill` | | | | |
| `:default` | | | | |
| `:checked` | | | | |
| `:indeterminate` | | | | |
| `:blank` (form control; MDN also lists a print-page `:blank` — same token, two contexts) | | | | |
| `:valid` | | | | |
| `:invalid` | | | | |
| `:in-range` | | | | |
| `:out-of-range` | | | | |
| `:required` | | | | |
| `:optional` | | | | |
| `:user-valid` | | | | |
| `:user-invalid` | | | | |
| `:dir()` | | | | |
| `:lang()` | | | | |
| `:any-link` | | | | |
| `:link` | | | | |
| `:visited` | | | | |
| `:local-link` | | | | |
| `:target` | | | | |
| `:scope` | | | | |
| `:playing` | | | | |
| `:paused` | | | | |
| `:seeking` | | | | |
| `:buffering` | | | | |
| `:stalled` | | | | |
| `:muted` | | | | |
| `:volume-locked` | | | | |
| `:current` | | | | |
| `:past` | | | | |
| `:future` | | | | |
| `:host` | | | | |
| `:host()` | | | | |
| `:host-context()` | | | | |
| `:has-slotted` | | | | |
| `:hover` | | | | |
| `:active` | | | | |
| `:focus` | | | | |
| `:focus-visible` | | | | |
| `:focus-within` | | | | |
| `:target-current` | | | | |
| `:is()` | | | | |
| `:not()` | | | | |
| `:where()` | | | | |
| `:has()` | | | | |
| `:state()` | | | | |
| `:first` (print page) | | | | |
| `:left` (print page) | | | | |
| `:right` (print page) | | | | |
| `:active-view-transition` | | | | |
| `:active-view-transition-type()` | | | | |
| `:interest-source` (experimental) | | | | |
| `:interest-target` (experimental) | | | | |
| `:target-after` (experimental) | | | | |
| `:target-before` (experimental) | | | | |
| `:-moz-broken` (non-standard) | | | | |
| `:-moz-drag-over` (non-standard) | | | | |
| `:-moz-first-node` (non-standard) | | | | |
| `:-moz-handler-blocked` (non-standard) | | | | |
| `:-moz-handler-crashed` (non-standard) | | | | |
| `:-moz-handler-disabled` (non-standard) | | | | |
| `:-moz-last-node` (non-standard) | | | | |
| `:-moz-loading` (non-standard) | | | | |
| `:-moz-locale-dir()` (non-standard) | | | | |
| `:-moz-only-whitespace` (non-standard) | | | | |
| `:-moz-submit-invalid` (non-standard) | | | | |
| `:-moz-suppressed` (non-standard) | | | | |
| `:-moz-user-disabled` (non-standard) | | | | |
| `:-moz-window-inactive` (non-standard) | | | | |

#### Pseudo-elements

Source: MDN's Pseudo-elements index page, current live categorisation (Typographic, Highlight, Tree-abiding, Element-backed, Form-related, View transitions), plus its non-standard vendor-prefixed section.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `::first-line` | | | | |
| `::first-letter` | | | | |
| `::cue` | | | | |
| `::cue()` | | | | |
| `::grammar-error` | | | | |
| `::highlight()` | | | | |
| `::search-text` | | | | |
| `::selection` | | | | |
| `::spelling-error` | | | | |
| `::target-text` | | | | |
| `::before` | | | | |
| `::after` | | | | |
| `::column` | | | | |
| `::marker` | | | | |
| `::backdrop` | | | | |
| `::scroll-button()` | | | | |
| `::scroll-marker` | | | | |
| `::scroll-marker-group` | | | | |
| `::details-content` | | | | |
| `::part()` | | | | |
| `::slotted()` | | | | |
| `::checkmark` | | | | |
| `::file-selector-button` | | | | |
| `::picker()` | | | | |
| `::picker-icon` | | | | |
| `::placeholder` | | | | |
| `::view-transition` | | | | |
| `::view-transition-image-pair()` | | | | |
| `::view-transition-group()` | | | | |
| `::view-transition-new()` | | | | |
| `::view-transition-old()` | | | | |
| `::-moz-color-swatch` (non-standard) | | | | |
| `::-moz-focus-inner` (non-standard) | | | | |
| `::-moz-list-bullet` (non-standard) | | | | |
| `::-moz-list-number` (non-standard) | | | | |
| `::-moz-meter-bar` (non-standard) | | | | |
| `::-moz-progress-bar` (non-standard) | | | | |
| `::-moz-range-progress` (non-standard) | | | | |
| `::-moz-range-thumb` (non-standard) | | | | |
| `::-moz-range-track` (non-standard) | | | | |
| `::-webkit-inner-spin-button` (non-standard) | | | | |
| `::-webkit-meter-bar` (non-standard) | | | | |
| `::-webkit-meter-even-less-good-value` (non-standard) | | | | |
| `::-webkit-meter-inner-element` (non-standard) | | | | |
| `::-webkit-meter-optimum-value` (non-standard) | | | | |
| `::-webkit-meter-suboptimum-value` (non-standard) | | | | |
| `::-webkit-progress-bar` (non-standard) | | | | |
| `::-webkit-progress-inner-element` (non-standard) | | | | |
| `::-webkit-progress-value` (non-standard) | | | | |
| `::-webkit-scrollbar` (non-standard) | | | | |
| `::-webkit-search-cancel-button` (non-standard) | | | | |
| `::-webkit-search-results-button` (non-standard) | | | | |
| `::-webkit-slider-runnable-track` (non-standard) | | | | |
| `::-webkit-slider-thumb` (non-standard) | | | | |

### 2c. At-rules and descriptors

Full at-rule list per MDN's CSS Reference "At-rules" index (22 entries): `@charset`, `@color-profile`, `@container`, `@counter-style`, `@custom-media`, `@document`, `@font-face`, `@font-feature-values`, `@font-palette-values`, `@function`, `@import`, `@keyframes`, `@layer`, `@media`, `@namespace`, `@page`, `@position-try`, `@property`, `@scope`, `@starting-style`, `@supports`, `@view-transition`.

#### @media

Condition/selector syntax (media queries), not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@media` (conditional block, media-query syntax) | | | | |

#### @import

Condition/selector syntax (URL + optional media query / layer / supports condition), not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@import` (stylesheet import statement) | | | | |

#### @charset

Statement at-rule, no descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@charset` (declares stylesheet text encoding) | | | | |

#### @namespace

Statement at-rule, no descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@namespace` (declares an XML namespace for selectors) | | | | |

#### @page

Descriptors implemented by at least one browser, per MDN. `@page` also hosts 16 page-margin at-rules and 4 page pseudo-classes (`:first`, `:left`, `:right`, `:blank` — already rowed in 2b Pseudo-classes, not duplicated here).

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `size` descriptor (target page size/orientation) | | | | |
| `margin` descriptor (shorthand) | | | | |
| `margin-top` descriptor | | | | |
| `margin-right` descriptor | | | | |
| `margin-bottom` descriptor | | | | |
| `margin-left` descriptor | | | | |
| `page-orientation` descriptor | | | | |
| Other CSS properties permitted by the Paged Media spec inside `@page` but not implemented by any browser (background/border/color/font/etc.) — group, unimplemented anywhere per MDN | | | | |
| Page-margin at-rules — group of 16: `@top-left-corner`, `@top-left`, `@top-center`, `@top-right`, `@top-right-corner`, `@bottom-left-corner`, `@bottom-left`, `@bottom-center`, `@bottom-right`, `@bottom-right-corner`, `@left-top`, `@left-middle`, `@left-bottom`, `@right-top`, `@right-middle`, `@right-bottom` | | | | |

#### @font-face

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `font-family` descriptor (required) | | | | |
| `src` descriptor (required) | | | | |
| `font-style` descriptor | | | | |
| `font-weight` descriptor (accepts a range) | | | | |
| `font-stretch` descriptor (accepts a range) | | | | |
| `font-width` descriptor | | | | |
| `font-display` descriptor | | | | |
| `font-feature-settings` descriptor | | | | |
| `font-variation-settings` descriptor | | | | |
| `unicode-range` descriptor | | | | |
| `ascent-override` descriptor | | | | |
| `descent-override` descriptor | | | | |
| `line-gap-override` descriptor | | | | |
| `size-adjust` descriptor | | | | |

#### @keyframes

Body is keyframe selectors (not descriptors).

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@keyframes` (named animation rule container) | | | | |
| Keyframe selector syntax inside the body: `from`, `to`, `<percentage>` (comma-separated lists allowed, e.g. `68%, 72%`) | | | | |

#### @supports

Condition syntax (feature queries), not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@supports` (feature-query conditional block) | | | | |

#### @font-feature-values

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `font-display` descriptor | | | | |
| `@stylistic` nested block | | | | |
| `@styleset` nested block | | | | |
| `@character-variant` nested block | | | | |
| `@swash` nested block | | | | |
| `@ornaments` nested block | | | | |
| `@annotation` nested block | | | | |

#### @counter-style

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `system` descriptor | | | | |
| `symbols` descriptor | | | | |
| `additive-symbols` descriptor | | | | |
| `negative` descriptor | | | | |
| `prefix` descriptor | | | | |
| `suffix` descriptor | | | | |
| `range` descriptor | | | | |
| `pad` descriptor | | | | |
| `speak-as` descriptor | | | | |
| `fallback` descriptor | | | | |

#### @property

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `syntax` descriptor (required) | | | | |
| `inherits` descriptor (required) | | | | |
| `initial-value` descriptor (required unless `syntax` is `"*"`) | | | | |

#### @layer

Statement/block at-rule declaring or assigning cascade layers, not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@layer` (named or anonymous cascade layer, statement or block form) | | | | |

#### @container

Condition/selector syntax (container queries), not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@container` (container-query conditional block, optionally named) | | | | |

#### @scope

Selector syntax (`(<scope-start>) to (<scope-end>)`), not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@scope` (scoped style block) | | | | |

#### @starting-style

Block at-rule wrapping ordinary declarations (entry-transition starting values), not descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@starting-style` (before-first-style values for transitions) | | | | |

#### @view-transition

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `navigation` descriptor | | | | |
| `types` descriptor | | | | |

#### @font-palette-values

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `font-family` descriptor (required) | | | | |
| `base-palette` descriptor | | | | |
| `override-colors` descriptor | | | | |

#### @position-try

Body accepts a fixed set of positioning properties rather than rule-specific descriptors.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@position-try` (named fallback position rule) | | | | |
| Body property set — group: `position-anchor`, `position-area`, inset properties (`top`/`right`/`bottom`/`left`, `inset-block-*`, `inset-inline-*`, `inset`), margin properties (`margin-*`), sizing properties (`width`/`height`/`min-*`/`max-*`/`block-size`/`inline-size`/etc.), `align-self`, `justify-self` | | | | |

#### @color-profile

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `src` descriptor | | | | |
| `rendering-intent` descriptor (`relative-colorimetric`/`absolute-colorimetric`/`perceptual`/`saturation`) | | | | |

#### @custom-media

Experimental (Media Queries Level 5); defines a single reusable media-query condition. No dedicated MDN reference page exists (fetch 404), sourced only from MDN's at-rules index.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@custom-media` (named reusable media-query condition, experimental) | | | | |

#### @document

Deprecated, non-standard (Firefox-only, never standardised beyond a Working Draft), still listed on MDN's at-rule index.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@document` (URL-matched conditional block, deprecated/non-standard) | | | | |

#### @function

Custom CSS function definition; body holds declared parameters plus one or more `result` descriptors instead of a fixed descriptor set.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `@function` (author-defined CSS function, with `--param <type>: default` parameter list and optional `returns <type>`) | | | | |
| `result` descriptor (the function's return value; may repeat under nested conditionals) | | | | |

### 2d. Value functions and keyword/colour value groups

Source: MDN's CSS value functions reference. Every function found is rowed below, organised into the sub-groups the task specified plus extra groups for functions MDN lists beyond that starter set (math extensions, anchor positioning, scroll/view timelines, tree counting, and a few others).

#### Math

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `calc()` | | | | |
| `calc-size()` | | | | |
| `min()` | | | | |
| `max()` | | | | |
| `clamp()` | | | | |
| `round()` | | | | |
| `mod()` | | | | |
| `rem()` | | | | |
| `sin()` | | | | |
| `cos()` | | | | |
| `tan()` | | | | |
| `asin()` | | | | |
| `acos()` | | | | |
| `atan()` | | | | |
| `atan2()` | | | | |
| `pow()` | | | | |
| `sqrt()` | | | | |
| `hypot()` | | | | |
| `log()` | | | | |
| `exp()` | | | | |
| `abs()` | | | | |
| `sign()` | | | | |
| `random()` | | | | |
| `progress()` | | | | |

#### Custom/environment

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `var()` | | | | |
| `env()` | | | | |
| `attr()` | | | | |
| `if()` | | | | |

#### Counters

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `counter()` | | | | |
| `counters()` | | | | |
| `symbols()` | | | | |

#### URLs/images

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `url()` | | | | |
| `image()` | | | | |
| `image-set()` | | | | |
| `cross-fade()` | | | | |
| `element()` | | | | |
| `paint()` | | | | |

#### Colour

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `rgb()` | | | | |
| `rgba()` (legacy alias, merged into the `rgb()` page on current MDN) | | | | |
| `hsl()` | | | | |
| `hsla()` (legacy alias, merged into the `hsl()` page on current MDN) | | | | |
| `hwb()` | | | | |
| `lab()` | | | | |
| `lch()` | | | | |
| `oklab()` | | | | |
| `oklch()` | | | | |
| `color()` | | | | |
| `color-mix()` | | | | |
| `light-dark()` | | | | |
| `contrast-color()` | | | | |
| `device-cmyk()` | | | | |
| `alpha()` | | | | |
| `dynamic-range-limit-mix()` | | | | |
| `palette-mix()` | | | | |

#### Gradients

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `linear-gradient()` | | | | |
| `radial-gradient()` | | | | |
| `conic-gradient()` | | | | |
| `repeating-linear-gradient()` | | | | |
| `repeating-radial-gradient()` | | | | |
| `repeating-conic-gradient()` | | | | |

#### Transform

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `translate()` | | | | |
| `translateX()` | | | | |
| `translateY()` | | | | |
| `translateZ()` | | | | |
| `translate3d()` | | | | |
| `scale()` | | | | |
| `scaleX()` | | | | |
| `scaleY()` | | | | |
| `scaleZ()` | | | | |
| `scale3d()` | | | | |
| `rotate()` | | | | |
| `rotateX()` | | | | |
| `rotateY()` | | | | |
| `rotateZ()` | | | | |
| `rotate3d()` | | | | |
| `skew()` | | | | |
| `skewX()` | | | | |
| `skewY()` | | | | |
| `matrix()` | | | | |
| `matrix3d()` | | | | |
| `perspective()` | | | | |

#### Easing

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `linear()` | | | | |
| `cubic-bezier()` | | | | |
| `steps()` | | | | |

#### Filter

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `blur()` | | | | |
| `brightness()` | | | | |
| `contrast()` | | | | |
| `drop-shadow()` | | | | |
| `grayscale()` | | | | |
| `hue-rotate()` | | | | |
| `invert()` | | | | |
| `opacity()` | | | | |
| `saturate()` | | | | |
| `sepia()` | | | | |

#### Shapes

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `circle()` | | | | |
| `ellipse()` | | | | |
| `inset()` | | | | |
| `polygon()` | | | | |
| `path()` | | | | |
| `rect()` | | | | |
| `xywh()` | | | | |
| `shape()` | | | | |
| `ray()` | | | | |
| `superellipse()` | | | | |

#### Grid/layout

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `repeat()` | | | | |
| `minmax()` | | | | |
| `fit-content()` | | | | |

#### Font (inside @font-face / font-variant-alternates)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `format()` | | | | |
| `local()` | | | | |
| `tech()` | | | | |
| `stylistic()` | | | | |
| `styleset()` | | | | |
| `character-variant()` | | | | |
| `swash()` | | | | |
| `ornaments()` | | | | |
| `annotation()` | | | | |

#### Anchor positioning

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `anchor()` | | | | |
| `anchor-size()` | | | | |

#### Scroll/view timelines

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `scroll()` | | | | |
| `view()` | | | | |

#### Tree counting

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `sibling-index()` | | | | |
| `sibling-count()` | | | | |

#### Other

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `layer()` (names a cascade layer inside `@import`) | | | | |
| `type()` (attribute type hint inside `attr()`) | | | | |
| `-moz-image-rect()` (non-standard, Firefox-only) | | | | |

#### Named colour keywords

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| The ~147 CSS extended named colour keywords — group, CSS Color Module Level 4 §named-colors, e.g. `red`, `cornflowerblue`, `rebeccapurple` (not enumerated individually) | | | | |

#### CSS-wide keyword values

Other per-property keyword values (e.g. `display: flex` vs `grid`) are covered inline within the properties table (2a), not duplicated here.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `inherit` | | | | |
| `initial` | | | | |
| `unset` | | | | |
| `revert` | | | | |
| `revert-layer` | | | | |

### 2e. Units

Source: MDN's CSS Values and Units page. Includes every unit the task's explicit list named, plus extras the page itself groups nearby (root-relative font units, small/large/dynamic viewport min/max/inline/block variants, and container query units — the last is defined in a separate module, CSS Containment, but MDN cross-lists it on this page).

#### Length — absolute

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `px` | | | | |
| `cm` | | | | |
| `mm` | | | | |
| `Q` | | | | |
| `in` | | | | |
| `pt` | | | | |
| `pc` | | | | |

#### Length — relative, font-based

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `em` | | | | |
| `rem` | | | | |
| `ex` | | | | |
| `ch` | | | | |
| `cap` | | | | |
| `ic` | | | | |
| `lh` | | | | |
| `rlh` | | | | |
| `rcap` (root cap height; extra found on MDN) | | | | |
| `rch` (root `ch`; extra found on MDN) | | | | |
| `rex` (root `ex`; extra found on MDN) | | | | |
| `ric` (root `ic`; extra found on MDN) | | | | |

#### Length — viewport

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `vw` | | | | |
| `vh` | | | | |
| `vmin` | | | | |
| `vmax` | | | | |
| `vi` | | | | |
| `vb` | | | | |
| `svw` | | | | |
| `svh` | | | | |
| `svmin` (extra found on MDN) | | | | |
| `svmax` (extra found on MDN) | | | | |
| `svi` (extra found on MDN) | | | | |
| `svb` (extra found on MDN) | | | | |
| `lvw` | | | | |
| `lvh` | | | | |
| `lvmin` (extra found on MDN) | | | | |
| `lvmax` (extra found on MDN) | | | | |
| `lvi` (extra found on MDN) | | | | |
| `lvb` (extra found on MDN) | | | | |
| `dvw` | | | | |
| `dvh` | | | | |
| `dvmin` (extra found on MDN) | | | | |
| `dvmax` (extra found on MDN) | | | | |
| `dvi` (extra found on MDN) | | | | |
| `dvb` (extra found on MDN) | | | | |

#### Length — container query (extra group, CSS Containment module, cross-listed on MDN's units page)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `cqw` | | | | |
| `cqh` | | | | |
| `cqi` | | | | |
| `cqb` | | | | |
| `cqmin` | | | | |
| `cqmax` | | | | |

#### Angle

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `deg` | | | | |
| `grad` | | | | |
| `rad` | | | | |
| `turn` | | | | |

#### Time

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `s` | | | | |
| `ms` | | | | |

#### Frequency

Defined by the CSS Values spec but not currently consumed by any shipped CSS property (reserved for potential future aural/speech features).

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `Hz` (unused by any current property) | | | | |
| `kHz` (unused by any current property) | | | | |

#### Resolution

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `dpi` | | | | |
| `dpcm` | | | | |
| `dppx` | | | | |
| `x` (alias for `dppx`, used in e.g. `image-set()`) | | | | |

#### Flex

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `fr` | | | | |

#### Percentage

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| `%` | | | | |

## Sources

- W3C canonical CSS property list (JSON) — https://www.w3.org/Style/CSS/all-properties.en.json — accessed 2026-09-23 (fetched in ~40 narrow exact-match calls, each requesting a fixed list of exact property names verbatim, plus FOUND/NOT-FOUND verification calls, to avoid the summarizer silently dropping or inventing entries — see "grouping and verification policy" below)
- MDN Pseudo-classes index — https://developer.mozilla.org/en-US/docs/Web/CSS/Pseudo-classes — accessed 2026-09-23
- MDN Pseudo-elements index — https://developer.mozilla.org/en-US/docs/Web/CSS/Pseudo-elements — accessed 2026-09-23
- MDN CSS_selectors overview — https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_selectors — accessed 2026-09-23
- MDN Attribute selectors reference — https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Selectors/Attribute_selectors — accessed 2026-09-23
- W3C Selectors Level 4 — https://www.w3.org/TR/selectors-4/ — accessed 2026-09-23
- MDN CSS_syntax/At-rule and CSS Reference at-rules index — https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_syntax/At-rule, https://developer.mozilla.org/en-US/docs/Web/CSS/Reference — accessed 2026-09-23
- MDN per-at-rule pages (@font-face, @page, @counter-style, @font-feature-values, @property, @font-palette-values, @position-try, @view-transition, @color-profile, @function, @keyframes) — accessed 2026-09-23
- MDN CSS_Values_and_Units/CSS_value_functions and CSS_Values_and_Units — accessed 2026-09-23
- Direct spot-check of `lighting-color` — https://developer.mozilla.org/en-US/docs/Web/CSS/lighting-color — accessed 2026-09-23 (see gap resolution below)

**Grouping and verification policy (2a):** the raw JSON has 1,087 entries because it repeats each property once per spec draft/version it appears in. This table de-duplicates to one row per distinct module FAMILY (cross-listed once per family when the JSON genuinely associates a property with more than one, e.g. `align-items` under both Box Alignment and Flexbox). The three CSS 2.1-era titles are collapsed into one "CSS 2.1 (Legacy)" section used only for properties with no modern-module entry. During research, ~18 plausible-sounding property names surfaced by an early free-form listing pass (border-start-end-radius, mask-border-*, font-synthesis-position, etc.) were checked with dedicated exact-match FOUND/NOT-FOUND calls and confirmed NOT present in the JSON — excluded from the table. Treat 414 as the number verified property-by-property.

**Gap flagged during research, and its resolution:** `lighting-color` was carried into the original property-name pass but its module/status was not independently re-verified in that session. Re-checked directly (2026-09-23): it is defined by Filter Effects Module Level 1 (`drafts.csswg.org/filter-effects-1/`), the same module as `filter`, currently at Working Draft status — row updated to `lighting-color (WD)` in the Filter Effects section above, matching its sibling `filter (WD)`.

**Other notes carried from research:** `box-shadow` is genuinely dual-homed (Backgrounds and Borders Level 3, CRD; Shadows Level 4, WD) — both sections are deliberate, not a duplicate. `contain`'s status line compresses three JSON entries (Level 1 REC, Level 2/3 WD) into one row. "CSS Positioned Layout Module Level 3" appeared under three different ED urls across fetches (css-positioning → css-position-3 → css-positioned-layout-3) with an identical `title` field — treated as one module, noted as a source-side inconsistency. `@custom-media` has no live MDN reference page (404) — its inclusion rests on MDN's at-rules index plus Media Queries Level 5 general knowledge. `rgba()`/`hsla()` no longer have their own MDN pages (merged into `rgb()`/`hsl()`) but are still rowed individually since the compiler will still encounter the literal tokens in author CSS.

## Counts

**887 total data rows** (verified by counting table rows in the assembled file — the subtotals below are approximate hand counts, kept for orientation): properties across 42 module sections (2a, ~414 distinct properties, some cross-listed under more than one module so the row count runs higher), selectors/at-rules/functions/units (2b/2c/2d/2e).
