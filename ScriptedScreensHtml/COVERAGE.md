# Coverage, measured (2026-09-15)

Handled sets are read from the code by `ScriptedScreensHtml.Tests/coverage.py`; references are MDN's CSS data, the HTML living standard's element list and a list of the DOM and Web APIs a page script commonly uses. A name counted as handled is one the code names; whether it behaves as a browser is what the console pages and examples check.


## CSS properties

**336 of 491 standard properties handled**, 155 not:

- CSS Backgrounds and Borders (20): background-origin background-position-x background-position-y border-shape corner-block-end-shape corner-block-start-shape corner-bottom-left-shape corner-bottom-right-shape corner-bottom-shape corner-end-end-shape corner-end-start-shape corner-inline-end-shape corner-inline-start-shape corner-left-shape corner-right-shape corner-start-end-shape corner-start-start-shape corner-top-left-shape corner-top-right-shape corner-top-shape
- CSS Masking (16): clip-rule mask-border mask-border-mode mask-border-outset mask-border-repeat mask-border-slice mask-border-source mask-border-width mask-clip mask-composite mask-mode mask-origin mask-position mask-repeat mask-size mask-type
- Scalable Vector Graphics (16): cx cy d marker marker-end marker-mid marker-start paint-order path-length r rx ry shape-rendering vector-effect x y
- CSS Animations (11): animation-trigger timeline-trigger timeline-trigger-activation-range timeline-trigger-activation-range-end timeline-trigger-activation-range-start timeline-trigger-active-range timeline-trigger-active-range-end timeline-trigger-active-range-start timeline-trigger-name timeline-trigger-source trigger-scope
- CSS Fonts (11): font-language-override font-palette font-size-adjust font-synthesis-small-caps font-synthesis-style font-synthesis-weight font-variant-alternates font-variant-east-asian font-variant-emoji font-variant-position font-variation-settings
- CSS Box Sizing (9): column-height column-width column-wrap contain-intrinsic-block-size contain-intrinsic-height contain-intrinsic-inline-size contain-intrinsic-size contain-intrinsic-width frame-sizing
- CSS Text (9): hyphenate-character hyphenate-limit-chars line-break text-autospace text-fit text-justify text-wrap-mode text-wrap-style white-space-collapse
- CSS Inline (7): alignment-baseline baseline-shift baseline-source initial-letter text-box text-box-edge text-box-trim
- CSS Basic User Interface (7): caret caret-animation caret-shape interactivity interest-delay interest-delay-end interest-delay-start
- CSS Multi-column Layout (6): column-fill column-rule column-rule-color column-rule-style column-rule-width column-span
- Motion Path (6): offset offset-anchor offset-distance offset-path offset-position offset-rotate
- CSS Text Decoration (5): text-decoration-inset text-emphasis text-emphasis-color text-emphasis-position text-emphasis-style
- Filter Effects (4): color-interpolation-filters flood-color flood-opacity lighting-color
- CSS Overflow (4): overflow-clip-margin scroll-axis-lock scroll-marker-group scroll-target-group
- CSS Shapes (3): shape-image-threshold shape-margin shape-outside
- CSS View Transitions (3): view-transition-class view-transition-name view-transition-scope
- MathML (2): math-depth math-style
- CSS Overscroll Behavior (2): overscroll-behavior-block overscroll-behavior-inline
- CSS Display (2): reading-flow reading-order
- Compositing and Blending (1): background-blend-mode
- CSS Fragmentation (1): box-decoration-break
- CSS Color (1): dynamic-range-limit
- CSS Flexible Box Layout (1): flex-line-count
- CSS Grid Layout (1): grid
- CSS Images (1): image-orientation
- CSS Scroll Anchoring (1): overflow-anchor
- CSS Paged Media (1): page
- CSS Ruby (1): ruby-overhang
- CSS Writing Modes (1): text-combine-upright
- CSS Transforms (1): transform-box
- CSS Transitions (1): transition-behavior

## CSS at-rules, selectors, functions, units

- at-rules handled: @-webkit-keyframes @charset @container @counter-style @font-face @import @keyframes @layer @media @property @scope @supports
- at-rules not handled: @font-feature-values @font-palette-values @namespace @page @starting-style @view-transition
- pseudo-classes and pseudo-elements handled: ::-webkit-scrollbar ::-webkit-scrollbar-thumb ::-webkit-scrollbar-track ::after ::before ::first-letter ::first-line ::marker ::placeholder :active :any-link :checked :default :dir :disabled :empty :enabled :first-child :first-of-type :focus :focus-visible :focus-within :has :hover :in-range :indeterminate :invalid :is :lang :last-child :last-of-type :link :matches :modal :not :nth-child :nth-last-child :nth-last-of-type :nth-of-type :only-child :only-of-type :open :optional :out-of-range :placeholder-shown :read-only :read-write :required :root :target :valid :visited :where
- not handled: ::backdrop ::checkmark ::cue ::details-content ::file-selector-button ::grammar-error ::highlight ::part ::picker ::picker-icon ::selection ::slotted ::spelling-error ::target-text ::view-transition ::view-transition-group ::view-transition-image-pair ::view-transition-new ::view-transition-old :active-view-transition :active-view-transition-type :autofill :buffering :defined :first :fullscreen :future :has-slotted :host :host-context :left :muted :past :paused :picture-in-picture :playing :popover-open :right :scope :seeking :stalled :state :user-invalid :user-valid :volume-locked
- functions handled (59 of 95): attr blur brightness calc circle clamp color color-mix conic-gradient contrast counter counters cubic-bezier drop-shadow ellipse fit-content grayscale hsl hue-rotate image inset invert layer linear linear-gradient matrix max min minmax opacity path perspective polygon radial-gradient rect rem repeating-conic-gradient repeating-linear-gradient repeating-radial-gradient rgb rotate rotateX rotateY rotateZ round saturate scale scaleX scaleY sepia skew skewX skewY steps symbols translate translateX translateY var
- functions not handled: abs acos alpha asin atan atan2 cos cross-fade env exp hwb hypot image-set lab lch light-dark log matrix3d mod oklab oklch paint palette-mix param pow ray rotate3d scale3d scaleZ sign sin sqrt tan translate3d translateZ xywh
- units handled: % ch cm deg em ex fr grad in mm ms pc pt px q rad rem s turn vh vmax vmin vw x
- units not handled: Hz Q cap dpcm dpi dppx ic kHz

## HTML elements

**93 of 112 elements handled**. Not handled: area base bdo col colgroup datalist dl embed hgroup iframe map menu object output picture search slot source track


## JavaScript: DOM and Web APIs

- **document**: 33 of 35. Missing: elementFromPoint write
- **element**: 96 of 100. Missing: compareDocumentPosition requestFullscreen setPointerCapture releasePointerCapture
- **window**: 65 of 80. Missing: fetch XMLHttpRequest WebSocket Worker postMessage Blob File FileReader FormData Headers Request Response AbortController DOMParser XMLSerializer
- **canvas**: 60 of 60. Missing: none
