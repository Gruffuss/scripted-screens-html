Rules and columns: see INSTRUCTION-SET.md.

## 3. JavaScript language

### 3a. Statements and declarations
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Block `{ }` | | | | |
| Empty statement `;` | | | | |
| Expression statement | | | | |
| `var` declaration | | | | |
| `let` declaration | | | | |
| `const` declaration | | | | |
| `using` declaration | | | | |
| `await using` declaration | | | | |
| `if...else` | | | | |
| `switch...case...default` | | | | |
| `for` | ✅ | a Lua `while` loop with its initialiser and update (`JsToLua.ForLoop`). A loop adding markup counting from a start the compile knows as one number, by a fixed step (`++`, `--`, `+=`, `-=`, `i = i ± s`), to a bound tested with `<`, `<=`, `>` or `>=`: a list laid out once at the most rows its bounds give, each row's index as JavaScript steps it (added up, so a fractional step rounds as it does there), and the rows shown counted on the chip by the loop's own steps (from 0 by 1: `math.ceil`/`math.floor` of the bound). Refused by name for now: a start that is one of several values, and a count loop that skips rows (`continue`) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "for loops counting from 1 with <=, down with > and with >= by -5, by a step of 2, and by a fraction"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a loop over a count from a fixed set, and a list filtered by a test that reads its index"; PlainTranslatorTests: "the script's own logic: arrays, a loop, Math, an object; a px width and a class from it"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-events.lua (the in-game page, not yet seen in game) | |
| `for...in` | | | | |
| `for...of` | | | | |
| `for await...of` | | | | |
| `while` | | | | |
| `do...while` | | | | |
| Function declaration | | | | |
| `async function` declaration | | | | |
| Generator function declaration `function*` | | | | |
| Async generator function declaration `async function*` | | | | |
| Class declaration | | | | |
| `try...catch` | | | | |
| `try...finally` | | | | |
| `try...catch...finally` | | | | |
| `throw` | | | | |
| `return` | | | | |
| `break` | | | | |
| `continue` (incl. labeled `continue label`) | | | | |
| `break label` (labeled break) | | | | |
| Labeled statement (`label:`) | | | | |
| `with` | | | | |
| `debugger` | | | | |
| Import declaration — default import (`import x from "m"`) | | | | |
| Import declaration — named import (`import { a } from "m"`) | | | | |
| Import declaration — named import with renaming (`import { a as b } from "m"`) | | | | |
| Import declaration — named import with string literal name (`import { "s" as b } from "m"`) | | | | |
| Import declaration — namespace import (`import * as ns from "m"`) | | | | |
| Import declaration — default + named combined (`import d, { a } from "m"`) | | | | |
| Import declaration — default + namespace combined (`import d, * as ns from "m"`) | | | | |
| Import declaration — side-effect-only import (`import "m"`) | | | | |
| Import declaration — import attributes (`import x from "m" with { type: "json" }`) | | | | |
| Import declaration — `import defer { a } from "m"` (phase modifier) | | | | |
| Import declaration — `import source { a } from "m"` (phase modifier) | | | | |
| `import.meta` (meta-property; note — this is an expression, see 3b) | | | | |
| Export declaration — named export via inline declaration (`export const x = 1`, `export function f(){}`, `export class C{}`) | | | | |
| Export declaration — export list (`export { a, b }`) | | | | |
| Export declaration — renaming exports (`export { a as b }`, `export { a as "string name" }`) | | | | |
| Export declaration — export as default via list (`export { a as default }`) | | | | |
| Export declaration — default export (`export default expr`, `export default function(){}`, `export default class{}`) | | | | |
| Export declaration — re-export named (`export { a, b } from "m"`) | | | | |
| Export declaration — re-export all (`export * from "m"`) | | | | |
| Export declaration — re-export all as namespace (`export * as ns from "m"`) | | | | |
| Export declaration — re-export default (`export { default, a } from "m"`, `export { default as name } from "m"`) | | | | |
| Hashbang / shebang comment (`#!...` as first line of script/module) | | | | |

### 3b. Expressions and operators
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Addition (`+`) | ✅ | `js_add` (numbers add, a string on either side concatenates as JavaScript prints each side); with a null (written, or a read that gives null) `v_nadd`, which prints it "null" in a text and counts it 0 in a sum. A variable holding null prints as undefined: Lua's nil is both | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a method called on a literal array, and null as text in a larger expression, a template and String()" | |
| Subtraction (`-`) | | | | |
| Multiplication (`*`) | | | | |
| Division (`/`) | | | | |
| Remainder (`%`) | | | | |
| Exponentiation (`**`) | | | | |
| Equality (`==`) | | | | |
| Inequality (`!=`) | | | | |
| Strict equality (`===`) | | | | |
| Strict inequality (`!==`) | | | | |
| Less than (`<`) | | | | |
| Greater than (`>`) | | | | |
| Less than or equal (`<=`) | | | | |
| Greater than or equal (`>=`) | | | | |
| Logical AND (`&&`) | | | | |
| Logical OR (`\|\|`) | ✅ | `js_or(a, function() return b end)`: b evaluated only when a is falsy; at compile time either side's object or fixed values (the right alone when the left is undefined), `list.find(…) \|\| list[0]` an item of the list | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-find.lua (the in-game page, not yet seen in game) | |
| Logical NOT (`!`) | | | | |
| Nullish coalescing (`??`) | ✅ | a Lua function called in place returning a, or b when a is nil; at compile time as `\|\|` | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Bitwise AND (`&`) | | | | |
| Bitwise OR (`\|`) | | | | |
| Bitwise XOR (`^`) | | | | |
| Bitwise NOT (`~`) | | | | |
| Left shift (`<<`) | | | | |
| Right shift (`>>`) | | | | |
| Unsigned right shift (`>>>`) | | | | |
| Assignment (`=`) | | | | |
| Addition assignment (`+=`) | | | | |
| Subtraction assignment (`-=`) | | | | |
| Multiplication assignment (`*=`) | | | | |
| Division assignment (`/=`) | | | | |
| Remainder assignment (`%=`) | | | | |
| Exponentiation assignment (`**=`) | | | | |
| Left shift assignment (`<<=`) | | | | |
| Right shift assignment (`>>=`) | | | | |
| Unsigned right shift assignment (`>>>=`) | | | | |
| Bitwise AND assignment (`&=`) | | | | |
| Bitwise OR assignment (`\|=`) | | | | |
| Bitwise XOR assignment (`^=`) | | | | |
| Logical AND assignment (`&&=`) | | | | |
| Logical OR assignment (`\|\|=`) | | | | |
| Logical nullish assignment (`??=`) | | | | |
| Comma operator (`,`) | | | | |
| Conditional (ternary) operator (`?:`) | | | | |
| Unary plus (`+x`) | | | | |
| Unary negation (`-x`) | | | | |
| Bitwise NOT unary (`~x`) | | | | |
| Logical NOT unary (`!x`) | | | | |
| `typeof` | | | | |
| `void` | | | | |
| `delete` | | | | |
| `in` operator | | | | |
| `instanceof` operator | | | | |
| Postfix increment (`A++`) | | | | |
| Postfix decrement (`A--`) | | | | |
| Prefix increment (`++A`) | | | | |
| Prefix decrement (`--A`) | | | | |
| `new` operator | | | | |
| `new.target` | | | | |
| Property accessor — dot notation (`obj.prop`) | | | | |
| Property accessor — bracket notation (`obj["prop"]`) | | | | |
| Optional chaining (`?.`), incl. `?.()` and `?.[]` forms | | | | |
| Function call expression (`f(args)`) | ✅ | a Lua call; a function kept in an array and called by an index (`acts[idx]()`) is called from the array (`js_m(acts, idx)`), and a helper called in markup is followed as markup (Element.innerHTML) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "clicks kept as functions in an array: a helper pushes each and returns the attribute data-act=…" and plain-acts.lua; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "innerHTML of elements from a helper function: text, a class and a style width from values" | |
| Dynamic import (`import(specifier)`) | | | | |
| `import.meta` | | | | |
| `import.defer(specifier)` | | | | |
| `import.source(specifier)` | | | | |
| Spread in function call arguments (`f(...args)`) | | | | |
| Spread in array literal (`[...arr]`) | | | | |
| Spread in object literal (`{...obj}`) | | | | |
| Rest parameters (`function f(...args)`) | | | | |
| Destructuring assignment — array pattern (`[a, b] = arr`) | | | | |
| Destructuring assignment — object pattern (`{a, b} = obj`) | | | | |
| Destructuring assignment — default values (`{a = 1} = obj`, `[a = 1] = arr`) | | | | |
| Destructuring assignment — nested patterns | | | | |
| Destructuring assignment — computed property keys (`{[key]: a} = obj`) | | | | |
| Destructuring assignment — rest element (`[a, ...rest] = arr`, `{a, ...rest} = obj`) | | | | |
| Arrow function expression | ✅ | a Lua function; one called where it is written (`(() => { … })()`) has what it returns followed at compile time as a function expression's is; a callback walking a list has each item of it as its item parameter; one handed to a helper in markup (`act(() => say(g.name))`) that reads a list's row keeps the row it was made in, each such name in a local of its own set as the function is made (`do local V_Kn = V_R1.a[0] … end`) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "clicks kept as functions in an array: a helper pushes each and returns the attribute data-act=…" and plain-acts.lua; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a table built by an immediately invoked function (arrow and function forms): its rows a list, their fields fixed values, a theme colour one too"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Function expression | ✅ | a Lua function; one called where it is written (`function () { … }()`) has what it returns followed at compile time - a list, an object whose fields are fixed values, a fixed value | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a table built by an immediately invoked function (arrow and function forms): its rows a list, their fields fixed values, a theme colour one too" | |
| `async function` expression | | | | |
| Generator function expression `function*` | | | | |
| Async generator function expression `async function*` | | | | |
| Class expression | | | | |
| `yield` | | | | |
| `yield*` | | | | |
| `await` | | | | |
| `this` | ✅ | in a click listener - a `function` given to `addEventListener('click')`, an `onclick` handler, an onclick attribute's code, and an arrow inside one (whose `this` is the function's): the element it listens on, as `e.currentTarget`; its number when it listens on one element, else `V_EV.currentTarget` read when the click reaches it. Refused by name: in a function also called some other way (its `this` there is not the element), in an arrow inside a listener on more than one element; elsewhere as before | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "onclick attributes and this: an attribute's code with this and event, one handing this to a function, one on an element with no id, a listener's this on two elements, an onclick property's this, an attribute's handler running before a listener the script adds, and one the script replaces"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-events.lua (the in-game page, not yet seen in game) | |
| `super(...)` call form | | | | |
| `super.prop` property access form | | | | |
| Template literal (`` `text ${expr}` ``) | ✅ | the pieces concatenated, each value through `js_str`; a null (written, or a read that gives null) prints "null" | PlainTranslatorTests: toFixed and a template literal; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a method called on a literal array, and null as text in a larger expression, a template and String()" | |
| Tagged template (`` tag`text ${expr}` ``) | | | | |
| Regular expression literal (`/ab+c/i`) | | | | |
| Object literal — shorthand properties (`{a}`) | | | | |
| Object literal — computed property names (`{[expr]: val}`) | | | | |
| Object literal — method shorthand (`{method() {}}`) | | | | |
| Object literal — getter (`{get x() {}}`) | | | | |
| Object literal — setter (`{set x(v) {}}`) | | | | |
| Object literal — spread properties (`{...obj}`) | | | | |
| Array literal (`[a, b]`), incl. elisions (`[, , a]`) | | | | |
| Grouping operator (`( )`) | | | | |

### 3c. Standard built-in objects (ECMA-262), one row per member

#### Global value and function properties (not tied to any object)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| globalThis | | | | |
| Infinity | | | | |
| NaN | | | | |
| undefined | | | | |
| eval | | | | |
| isFinite | | | | |
| isNaN | | | | |
| parseFloat | | | | |
| parseInt | | | | |
| encodeURI | | | | |
| encodeURIComponent | | | | |
| decodeURI | | | | |
| decodeURIComponent | | | | |

#### Object

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Object() (constructor) | | | | |
| Object.assign | ✅ | the prelude's `Object.assign`: each source's own fields copied onto the target, which it returns (sources that are not tables skipped). A list held in a field of the target is followed through it: each object a source can be (a helper's `patch` parameter, an object written inline at the call) gives that field its value | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log kept in a state object: newest first with concat, trimmed by a slice, the object patched through Object.assign and read back through the values a helper returns"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-fields.lua (the in-game page, seen in game 2026-09-24) | |
| Object.create | | | | |
| Object.defineProperties | | | | |
| Object.defineProperty | | | | |
| Object.entries | | | | |
| Object.freeze | | | | |
| Object.fromEntries | | | | |
| Object.getOwnPropertyDescriptor | | | | |
| Object.getOwnPropertyDescriptors | | | | |
| Object.getOwnPropertyNames | | | | |
| Object.getOwnPropertySymbols | | | | |
| Object.getPrototypeOf | | | | |
| Object.groupBy | | | | |
| Object.hasOwn | | | | |
| Object.is | | | | |
| Object.isExtensible | | | | |
| Object.isFrozen | | | | |
| Object.isSealed | | | | |
| Object.keys | | | | |
| Object.preventExtensions | | | | |
| Object.seal | | | | |
| Object.setPrototypeOf | | | | |
| Object.values | | | | |
| Object.prototype.__proto__ | | | | |
| Object.prototype.constructor | | | | |
| Object.prototype.__defineGetter__ | | | | |
| Object.prototype.__defineSetter__ | | | | |
| Object.prototype.__lookupGetter__ | | | | |
| Object.prototype.__lookupSetter__ | | | | |
| Object.prototype.hasOwnProperty | | | | |
| Object.prototype.isPrototypeOf | | | | |
| Object.prototype.propertyIsEnumerable | | | | |
| Object.prototype.toLocaleString | | | | |
| Object.prototype.toString | | | | |
| Object.prototype.valueOf | | | | |

#### Function

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Function() (constructor) | | | | |
| Function.prototype.apply | | | | |
| Function.prototype.bind | | | | |
| Function.prototype.call | | | | |
| Function.prototype.toString | | | | |
| Function.prototype[Symbol.hasInstance] | | | | |
| Function.prototype.arguments | | | | |
| Function.prototype.caller | | | | |
| Function.prototype.constructor | | | | |
| Function.prototype.length | | | | |
| Function.prototype.name | | | | |
| Function.prototype.prototype | | | | |

#### Boolean

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Boolean() (constructor) | | | | |
| Boolean.prototype.toString | ✅ | "true" or "false": `BooleanMethods.toString` through `js_m`. A boolean (or undefined, or an array) written into text by any route - `+`, a template, a whole `textContent` - goes to the scene as JavaScript prints it (`v_txt`, `js_str`), never as a Lua boolean, which the vector mod does not read | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "booleans and undefined written into text: every(), includes(), a comparison, a boolean held in a name, toString(), valueOf() and String() of one, a variable never set"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-bool.lua (the in-game page) | |
| Boolean.prototype.valueOf | ✅ | the boolean itself (`BooleanMethods.valueOf`) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "booleans and undefined written into text: every(), includes(), a comparison, a boolean held in a name, toString(), valueOf() and String() of one, a variable never set" | |
| Boolean.prototype.constructor | | | | |

#### Symbol

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Symbol() (constructor, no `new`) | | | | |
| Symbol.for | | | | |
| Symbol.keyFor | | | | |
| Symbol.iterator | | | | |
| Symbol.asyncIterator | | | | |
| Symbol.hasInstance | | | | |
| Symbol.isConcatSpreadable | | | | |
| Symbol.species | | | | |
| Symbol.toPrimitive | | | | |
| Symbol.toStringTag | | | | |
| Symbol.unscopables | | | | |
| Symbol.match | | | | |
| Symbol.replace | | | | |
| Symbol.search | | | | |
| Symbol.split | | | | |
| Symbol.dispose | | | | |
| Symbol.asyncDispose | | | | |
| Symbol.prototype.constructor | | | | |
| Symbol.prototype.description | | | | |
| Symbol.prototype[Symbol.toStringTag] | | | | |
| Symbol.prototype.toString | | | | |
| Symbol.prototype.valueOf | | | | |
| Symbol.prototype[Symbol.toPrimitive] | | | | |

#### Error

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Error() (constructor) | | | | |
| Error.prototype.constructor | | | | |
| Error.prototype.name | | | | |
| Error.prototype.message | | | | |
| Error.prototype.cause | | | | |
| Error.prototype.toString | | | | |
| Error.prototype.stack (non-standard, de facto universal) | | | | |

#### NativeError subtypes (AggregateError, EvalError, RangeError, ReferenceError, SyntaxError, TypeError, URIError)

Each subtype's `.prototype` fully inherits `Error.prototype` (name/message/cause/toString/stack) unchanged — see the Error section above; not re-listed per subtype. Only each subtype's own constructor, and any member unique to that subtype, get a row.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| AggregateError() (constructor) | | | | |
| AggregateError.prototype.errors | | | | |
| EvalError() (constructor) | | | | |
| RangeError() (constructor) | | | | |
| ReferenceError() (constructor) | | | | |
| SyntaxError() (constructor) | | | | |
| TypeError() (constructor) | | | | |
| URIError() (constructor) | | | | |

#### Number

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Number() (constructor) | | | | |
| Number.EPSILON | | | | |
| Number.MAX_SAFE_INTEGER | | | | |
| Number.MAX_VALUE | | | | |
| Number.MIN_SAFE_INTEGER | | | | |
| Number.MIN_VALUE | | | | |
| Number.NaN | | | | |
| Number.NEGATIVE_INFINITY | | | | |
| Number.POSITIVE_INFINITY | | | | |
| Number.isFinite | | | | |
| Number.isInteger | | | | |
| Number.isNaN | | | | |
| Number.isSafeInteger | | | | |
| Number.parseFloat | | | | |
| Number.parseInt | | | | |
| Number.prototype.constructor | | | | |
| Number.prototype.toExponential | | | | |
| Number.prototype.toFixed | ✅ | `%.nf` in a text placeholder, or `v_fixed` (rounds as JS does) | PlainTranslatorTests: toFixed and a template literal; plain-counter in game | |
| Number.prototype.toLocaleString | | | | |
| Number.prototype.toPrecision | | | | |
| Number.prototype.toString | | | | |
| Number.prototype.valueOf | | | | |

#### BigInt

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| BigInt() (constructor, no `new`) | | | | |
| BigInt.asIntN | | | | |
| BigInt.asUintN | | | | |
| BigInt.prototype.constructor | | | | |
| BigInt.prototype[Symbol.toStringTag] | | | | |
| BigInt.prototype.toLocaleString | | | | |
| BigInt.prototype.toString | | | | |
| BigInt.prototype.valueOf | | | | |

#### Math (not constructible)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Math.E | | | | |
| Math.LN10 | | | | |
| Math.LN2 | | | | |
| Math.LOG10E | | | | |
| Math.LOG2E | | | | |
| Math.PI | | | | |
| Math.SQRT1_2 | | | | |
| Math.SQRT2 | | | | |
| Math.abs | | | | |
| Math.acos | | | | |
| Math.acosh | | | | |
| Math.asin | | | | |
| Math.asinh | | | | |
| Math.atan | | | | |
| Math.atan2 | | | | |
| Math.atanh | | | | |
| Math.cbrt | | | | |
| Math.ceil | | | | |
| Math.clz32 | | | | |
| Math.cos | | | | |
| Math.cosh | | | | |
| Math.exp | | | | |
| Math.expm1 | | | | |
| Math.f16round | | | | |
| Math.floor | | | | |
| Math.fround | | | | |
| Math.hypot | | | | |
| Math.imul | | | | |
| Math.log | | | | |
| Math.log10 | | | | |
| Math.log1p | | | | |
| Math.log2 | | | | |
| Math.max | | | | |
| Math.min | | | | |
| Math.pow | | | | |
| Math.random | | | | |
| Math.round | | | | |
| Math.sign | | | | |
| Math.sin | | | | |
| Math.sinh | | | | |
| Math.sqrt | | | | |
| Math.sumPrecise | | | | |
| Math.tan | | | | |
| Math.tanh | | | | |
| Math.trunc | | | | |

#### Date

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Date() (constructor) | | | | |
| Date.now | | | | |
| Date.parse | | | | |
| Date.UTC | | | | |
| Date.prototype.constructor | | | | |
| Date.prototype.getDate | | | | |
| Date.prototype.getDay | | | | |
| Date.prototype.getFullYear | | | | |
| Date.prototype.getHours | | | | |
| Date.prototype.getMilliseconds | | | | |
| Date.prototype.getMinutes | | | | |
| Date.prototype.getMonth | | | | |
| Date.prototype.getSeconds | | | | |
| Date.prototype.getTime | | | | |
| Date.prototype.getTimezoneOffset | | | | |
| Date.prototype.getYear (deprecated, Annex B) | | | | |
| Date.prototype.getUTCDate | | | | |
| Date.prototype.getUTCDay | | | | |
| Date.prototype.getUTCFullYear | | | | |
| Date.prototype.getUTCHours | | | | |
| Date.prototype.getUTCMilliseconds | | | | |
| Date.prototype.getUTCMinutes | | | | |
| Date.prototype.getUTCMonth | | | | |
| Date.prototype.getUTCSeconds | | | | |
| Date.prototype.setDate | | | | |
| Date.prototype.setFullYear | | | | |
| Date.prototype.setHours | | | | |
| Date.prototype.setMilliseconds | | | | |
| Date.prototype.setMinutes | | | | |
| Date.prototype.setMonth | | | | |
| Date.prototype.setSeconds | | | | |
| Date.prototype.setTime | | | | |
| Date.prototype.setYear (deprecated, Annex B) | | | | |
| Date.prototype.setUTCDate | | | | |
| Date.prototype.setUTCFullYear | | | | |
| Date.prototype.setUTCHours | | | | |
| Date.prototype.setUTCMilliseconds | | | | |
| Date.prototype.setUTCMinutes | | | | |
| Date.prototype.setUTCMonth | | | | |
| Date.prototype.setUTCSeconds | | | | |
| Date.prototype.toString | | | | |
| Date.prototype.toDateString | | | | |
| Date.prototype.toTimeString | | | | |
| Date.prototype.toISOString | | | | |
| Date.prototype.toJSON | | | | |
| Date.prototype.toUTCString | | | | |
| Date.prototype.toLocaleDateString | | | | |
| Date.prototype.toLocaleTimeString | | | | |
| Date.prototype.toLocaleString | | | | |
| Date.prototype.valueOf | | | | |
| Date.prototype[Symbol.toPrimitive] | | | | |

#### String

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| String() (constructor) | ✅ | called as a function: the prelude's `String`, which is `js_str`; a null (written, or a read that gives null) is "null" | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a method called on a literal array, and null as text in a larger expression, a template and String()" | |
| String.fromCharCode | | | | |
| String.fromCodePoint | | | | |
| String.raw | | | | |
| String.prototype.length | | | | |
| String.prototype.constructor | | | | |
| String.prototype.at | | | | |
| String.prototype.charAt | | | | |
| String.prototype.charCodeAt | | | | |
| String.prototype.codePointAt | | | | |
| String.prototype.includes | | | | |
| String.prototype.indexOf | | | | |
| String.prototype.lastIndexOf | | | | |
| String.prototype.match | | | | |
| String.prototype.matchAll | | | | |
| String.prototype.search | | | | |
| String.prototype.startsWith | | | | |
| String.prototype.endsWith | | | | |
| String.prototype.concat | | | | |
| String.prototype.repeat | | | | |
| String.prototype.replace | | | | |
| String.prototype.replaceAll | | | | |
| String.prototype.slice | | | | |
| String.prototype.split | | | | |
| String.prototype.substring | | | | |
| String.prototype.substr (Annex B, legacy) | | | | |
| String.prototype.trim | | | | |
| String.prototype.trimStart | | | | |
| String.prototype.trimEnd | | | | |
| String.prototype.padStart | | | | |
| String.prototype.padEnd | | | | |
| String.prototype.toLowerCase | | | | |
| String.prototype.toUpperCase | | | | |
| String.prototype.toLocaleLowerCase | | | | |
| String.prototype.toLocaleUpperCase | | | | |
| String.prototype.normalize | | | | |
| String.prototype.isWellFormed | | | | |
| String.prototype.toWellFormed | | | | |
| String.prototype.localeCompare | | | | |
| String.prototype.toString | | | | |
| String.prototype.valueOf | | | | |
| String.prototype[Symbol.iterator] | | | | |
| String.prototype.anchor (deprecated, Annex B) | | | | |
| String.prototype.big (deprecated, Annex B) | | | | |
| String.prototype.blink (deprecated, Annex B) | | | | |
| String.prototype.bold (deprecated, Annex B) | | | | |
| String.prototype.fixed (deprecated, Annex B) | | | | |
| String.prototype.fontcolor (deprecated, Annex B) | | | | |
| String.prototype.fontsize (deprecated, Annex B) | | | | |
| String.prototype.italics (deprecated, Annex B) | | | | |
| String.prototype.link (deprecated, Annex B) | | | | |
| String.prototype.small (deprecated, Annex B) | | | | |
| String.prototype.strike (deprecated, Annex B) | | | | |
| String.prototype.sub (deprecated, Annex B) | | | | |
| String.prototype.sup (deprecated, Annex B) | | | | |

#### RegExp

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| RegExp() (constructor) | | | | |
| RegExp.escape | | | | |
| RegExp[Symbol.species] | | | | |
| RegExp legacy static properties ($1-$9, input/lastMatch/lastParen/leftContext/rightContext, all non-standard Annex-B-adjacent) | | | | |
| RegExp.prototype.constructor | | | | |
| RegExp.prototype.dotAll | | | | |
| RegExp.prototype.flags | | | | |
| RegExp.prototype.global | | | | |
| RegExp.prototype.hasIndices | | | | |
| RegExp.prototype.ignoreCase | | | | |
| RegExp.prototype.multiline | | | | |
| RegExp.prototype.source | | | | |
| RegExp.prototype.sticky | | | | |
| RegExp.prototype.unicode | | | | |
| RegExp.prototype.unicodeSets | | | | |
| RegExp.prototype.lastIndex (own instance property) | | | | |
| RegExp.prototype.compile (non-standard, Annex-B-adjacent) | | | | |
| RegExp.prototype.exec | | | | |
| RegExp.prototype.test | | | | |
| RegExp.prototype.toString | | | | |
| RegExp.prototype[Symbol.match] | | | | |
| RegExp.prototype[Symbol.matchAll] | | | | |
| RegExp.prototype[Symbol.replace] | | | | |
| RegExp.prototype[Symbol.search] | | | | |
| RegExp.prototype[Symbol.split] | | | | |

#### Array

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Array() (constructor) | | | | |
| Array.from | | | | |
| Array.fromAsync | | | | |
| Array.isArray | | | | |
| Array.of | | | | |
| Array[Symbol.species] | | | | |
| Array.prototype.length | ✅ | read: `js_len`; written: `js_setlen`, which drops the items past the new length (or leaves holes, undefined, when longer) and throws RangeError for a length that is not a whole number, as JavaScript does | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list inside a box that shows only while the list has rows, built by a helper that adds markup in steps; rows kept by a ternary that reads the index" | |
| Array.prototype.constructor | | | | |
| Array.prototype[Symbol.unscopables] | | | | |
| Array.prototype.at | | | | |
| Array.prototype.concat | ✅ | the prelude's `ArrayMethods.concat`; for a list of markup, as long as its parts together (an array read from a name, a call or an object's field), `[x].concat(a).slice(0, N)` holding it to N | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "lists bounded other ways: a splice trim, and concat then slice"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log kept in a state object: newest first with concat, trimmed by a slice, the object patched through Object.assign and read back through the values a helper returns" | |
| Array.prototype.slice | ✅ | before `.map` in markup: bounds of the rows (`v_slice`), a slice of fixed size bounding an array nothing else bounds; elsewhere the prelude's `ArrayMethods.slice` | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "markup added to the element with += in a for...of, a string built in a forEach with a class from the index, and a slice of a list that is never held to a length" | |
| Array.prototype.copyWithin | | | | |
| Array.prototype.fill | | | | |
| Array.prototype.pop | ✅ | the prelude's `ArrayMethods.pop`; after an addition, `if (a.length > N) a.pop()` holds a list to N | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log that grows to a cap, newest first, with a message while it is empty and a paragraph after it that moves"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-list.lua (the in-game page, not yet seen in game) | |
| Array.prototype.push | ✅ | the prelude's `ArrayMethods.push`; for a list of markup, an addition held by an `if (a.length < N)` before it or a trim after it, or made in loops over bounded lists into an array made fresh each time (anything else leaves the list unbounded, refused by name); a push a helper does while markup is built (`acts.push(fn)` before returning ` data-act="…"`) is done right before the write the helper's value lands in, so the index it returns is its own | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "clicks kept as functions in an array: a helper pushes each and returns the attribute data-act=…" and plain-acts.lua; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list inside a box that shows only while the list has rows, built by a helper that adds markup in steps; rows kept by a ternary that reads the index"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "lists bounded other ways: an array made fresh and filled in a loop over a bounded list, and a while trim"; PlainTranslatorTests refusals: "a list whose array grows with nothing holding it" | |
| Array.prototype.reverse | | | | |
| Array.prototype.shift | ✅ | the prelude's `ArrayMethods.shift`; after an addition, `if (a.length > N) a.shift()` or `while (a.length > N) a.shift()` holds a list to N | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list inside a box that shows only while the list has rows, built by a helper that adds markup in steps; rows kept by a ternary that reads the index"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "lists bounded other ways: an array made fresh and filled in a loop over a bounded list, and a while trim" | |
| Array.prototype.sort | | | | |
| Array.prototype.splice | ✅ | the prelude's `ArrayMethods.splice`; taking items out leaves a list's bound as it was, and `a.splice(0, a.length - N)` after an addition holds it to N | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "lists bounded other ways: a splice trim, and concat then slice" | |
| Array.prototype.unshift | ✅ | the prelude's `ArrayMethods.unshift`; for a list of markup, held to a length as push is | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log that grows to a cap, newest first, with a message while it is empty and a paragraph after it that moves"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "markup added to the element with += in a for...of, a string built in a forEach with a class from the index, and a slice of a list that is never held to a length"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-list.lua (the in-game page, not yet seen in game) | |
| Array.prototype.every | ✅ | the prelude's `ArrayMethods.every` through `js_m`, the callback a Lua function, a Lua boolean | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Array.prototype.filter | ✅ | before `.map` in markup: a test run item by item as the rows are counted, its item and index bound (`Boolean` too); elsewhere the prelude's `ArrayMethods.filter`; for a list's bound, as long as what it filters, and `a = a.filter(…)` adds nothing | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "rows whose markup depends on their item, after a filter: each row its own shape, the rows after a filtered one moving up"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a loop over a count from a fixed set, and a list filtered by a test that reads its index" | |
| Array.prototype.find | ✅ | the prelude's `ArrayMethods.find` through `js_m`, the callback a Lua function; at compile time the item found is any item of a list the compile knows, so its fields are fixed values and its own lists are lists (with `||` or `??` a fallback when nothing is found) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-find.lua (the in-game page, not yet seen in game) | |
| Array.prototype.findIndex | ✅ | the prelude's `ArrayMethods.findIndex` through `js_m`, the callback a Lua function (-1 when nothing is found) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-find.lua (the in-game page, not yet seen in game) | |
| Array.prototype.findLast | ✅ | the prelude's `ArrayMethods.findLast` through `js_m`, the callback a Lua function; at compile time any item of the list, as find | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Array.prototype.findLastIndex | | | | |
| Array.prototype.flat | | | | |
| Array.prototype.flatMap | | | | |
| Array.prototype.forEach | ✅ | the prelude's `ArrayMethods.forEach` through `js_m`, carried into the chunk when the script calls it - on a name or on an array written in place (`[1, 2].forEach(f)`) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a method called on a literal array, and null as text in a larger expression, a template and String()"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "querySelector and querySelectorAll: a list's length, item(), an index and forEach" | |
| Array.prototype.map | ✅ | in markup written with innerHTML, `.map(cb).join(sep)` is a list of rows compiled away (see Element.innerHTML — a list repeated over an array in INSTRUCTION-SET-DOM.md); elsewhere the prelude's `ArrayMethods.map` through `js_m`, which makes the array it returns as JavaScript does; at compile time each item of what map gives is what the callback returns for an item of the list, that item bound to its parameter, so the fields of those items are fixed values | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "fields of a list's items as fixed values: a log in a state object whose rows take their colour and class from each entry, and rows made by a map callback"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a log that grows to a cap, newest first, with a message while it is empty and a paragraph after it that moves"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a list read from a field of an object, and a helper's list given as an argument or left to its default with ||" | |
| Array.prototype.reduce | | | | |
| Array.prototype.reduceRight | | | | |
| Array.prototype.some | ✅ | the prelude's `ArrayMethods.some` through `js_m`, the callback a Lua function, a Lua boolean | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Array.prototype.includes | ✅ | the prelude's `ArrayMethods.includes` through `js_m`, a Lua boolean | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Array.prototype.indexOf | ✅ | the prelude's `ArrayMethods.indexOf` through `js_m` (-1 when absent) | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read" | |
| Array.prototype.lastIndexOf | | | | |
| Array.prototype.join | ✅ | after `.map` in markup: the text between rows, part of each row after the first (white space only between elements); elsewhere the prelude's `ArrayMethods.join` | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "markup added to the element with += in a for...of, a string built in a forEach with a class from the index, and a slice of a list that is never held to a length" | |
| Array.prototype.toLocaleString | | | | |
| Array.prototype.toString | | | | |
| Array.prototype.toReversed | | | | |
| Array.prototype.toSorted | | | | |
| Array.prototype.toSpliced | | | | |
| Array.prototype.with | | | | |
| Array.prototype.entries | | | | |
| Array.prototype.keys | | | | |
| Array.prototype.values | | | | |
| Array.prototype[Symbol.iterator] | | | | |

#### TypedArray (%TypedArray% abstract base, shared by all concrete typed array constructors)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| %TypedArray% has no exposed global constructor of its own (abstract) | | | | |
| TypedArray.from | | | | |
| TypedArray.of | | | | |
| TypedArray[Symbol.species] | | | | |
| TypedArray.BYTES_PER_ELEMENT (static) | | | | |
| TypedArray.prototype.buffer | | | | |
| TypedArray.prototype.byteLength | | | | |
| TypedArray.prototype.byteOffset | | | | |
| TypedArray.prototype.constructor | | | | |
| TypedArray.prototype.length | | | | |
| TypedArray.prototype.BYTES_PER_ELEMENT | | | | |
| TypedArray.prototype[Symbol.toStringTag] | | | | |
| TypedArray.prototype.at | | | | |
| TypedArray.prototype.copyWithin | | | | |
| TypedArray.prototype.entries | | | | |
| TypedArray.prototype.every | | | | |
| TypedArray.prototype.fill | | | | |
| TypedArray.prototype.filter | | | | |
| TypedArray.prototype.find | | | | |
| TypedArray.prototype.findIndex | | | | |
| TypedArray.prototype.findLast | | | | |
| TypedArray.prototype.findLastIndex | | | | |
| TypedArray.prototype.forEach | | | | |
| TypedArray.prototype.includes | | | | |
| TypedArray.prototype.indexOf | | | | |
| TypedArray.prototype.join | | | | |
| TypedArray.prototype.keys | | | | |
| TypedArray.prototype.lastIndexOf | | | | |
| TypedArray.prototype.map | | | | |
| TypedArray.prototype.reduce | | | | |
| TypedArray.prototype.reduceRight | | | | |
| TypedArray.prototype.reverse | | | | |
| TypedArray.prototype.set | | | | |
| TypedArray.prototype.slice | | | | |
| TypedArray.prototype.some | | | | |
| TypedArray.prototype.sort | | | | |
| TypedArray.prototype.subarray | | | | |
| TypedArray.prototype.toLocaleString | | | | |
| TypedArray.prototype.toReversed | | | | |
| TypedArray.prototype.toSorted | | | | |
| TypedArray.prototype.toString | | | | |
| TypedArray.prototype.values | | | | |
| TypedArray.prototype.with | | | | |
| TypedArray.prototype[Symbol.iterator] | | | | |
| **Grouped:** identical %TypedArray%.prototype surface (all rows above) on all 11 concrete constructors — Int8Array, Uint8Array, Uint8ClampedArray, Int16Array, Uint16Array, Int32Array, Uint32Array, Float32Array, Float64Array, BigInt64Array, BigUint64Array — each with its own `.BYTES_PER_ELEMENT` constant and element type, not re-listed per constructor | | | | |

#### Map

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Map() (constructor) | | | | |
| Map.groupBy | | | | |
| Map[Symbol.species] | | | | |
| Map.prototype.constructor | | | | |
| Map.prototype.size | | | | |
| Map.prototype[Symbol.toStringTag] | | | | |
| Map.prototype.clear | | | | |
| Map.prototype.delete | | | | |
| Map.prototype.entries | | | | |
| Map.prototype.forEach | | | | |
| Map.prototype.get | | | | |
| Map.prototype.getOrInsert | | | | |
| Map.prototype.getOrInsertComputed | | | | |
| Map.prototype.has | | | | |
| Map.prototype.keys | | | | |
| Map.prototype.set | | | | |
| Map.prototype.values | | | | |
| Map.prototype[Symbol.iterator] | | | | |

#### Set

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Set() (constructor) | | | | |
| Set[Symbol.species] | | | | |
| Set.prototype.constructor | | | | |
| Set.prototype.size | | | | |
| Set.prototype[Symbol.toStringTag] | | | | |
| Set.prototype.add | | | | |
| Set.prototype.clear | | | | |
| Set.prototype.delete | | | | |
| Set.prototype.difference | | | | |
| Set.prototype.entries | | | | |
| Set.prototype.forEach | | | | |
| Set.prototype.has | | | | |
| Set.prototype.intersection | | | | |
| Set.prototype.isDisjointFrom | | | | |
| Set.prototype.isSubsetOf | | | | |
| Set.prototype.isSupersetOf | | | | |
| Set.prototype.keys | | | | |
| Set.prototype.symmetricDifference | | | | |
| Set.prototype.union | | | | |
| Set.prototype.values | | | | |
| Set.prototype[Symbol.iterator] | | | | |

#### WeakMap

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| WeakMap() (constructor) | | | | |
| WeakMap.prototype.constructor | | | | |
| WeakMap.prototype[Symbol.toStringTag] | | | | |
| WeakMap.prototype.delete | | | | |
| WeakMap.prototype.get | | | | |
| WeakMap.prototype.getOrInsert | | | | |
| WeakMap.prototype.getOrInsertComputed | | | | |
| WeakMap.prototype.has | | | | |
| WeakMap.prototype.set | | | | |

#### WeakSet

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| WeakSet() (constructor) | | | | |
| WeakSet.prototype.constructor | | | | |
| WeakSet.prototype[Symbol.toStringTag] | | | | |
| WeakSet.prototype.add | | | | |
| WeakSet.prototype.delete | | | | |
| WeakSet.prototype.has | | | | |

#### ArrayBuffer

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| ArrayBuffer() (constructor) | | | | |
| ArrayBuffer.isView | | | | |
| ArrayBuffer[Symbol.species] | | | | |
| ArrayBuffer.prototype.byteLength | | | | |
| ArrayBuffer.prototype.constructor | | | | |
| ArrayBuffer.prototype.detached | | | | |
| ArrayBuffer.prototype.maxByteLength | | | | |
| ArrayBuffer.prototype.resizable | | | | |
| ArrayBuffer.prototype[Symbol.toStringTag] | | | | |
| ArrayBuffer.prototype.resize | | | | |
| ArrayBuffer.prototype.slice | | | | |
| ArrayBuffer.prototype.transfer | | | | |
| ArrayBuffer.prototype.transferToFixedLength | | | | |

#### SharedArrayBuffer

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| SharedArrayBuffer() (constructor) | | | | |
| SharedArrayBuffer[Symbol.species] | | | | |
| SharedArrayBuffer.prototype.byteLength | | | | |
| SharedArrayBuffer.prototype.constructor | | | | |
| SharedArrayBuffer.prototype.growable | | | | |
| SharedArrayBuffer.prototype.maxByteLength | | | | |
| SharedArrayBuffer.prototype[Symbol.toStringTag] | | | | |
| SharedArrayBuffer.prototype.grow | | | | |
| SharedArrayBuffer.prototype.slice | | | | |

#### DataView

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| DataView() (constructor) | | | | |
| DataView.prototype.buffer | | | | |
| DataView.prototype.byteLength | | | | |
| DataView.prototype.byteOffset | | | | |
| DataView.prototype.constructor | | | | |
| DataView.prototype[Symbol.toStringTag] | | | | |
| DataView.prototype.getBigInt64 | | | | |
| DataView.prototype.getBigUint64 | | | | |
| DataView.prototype.getFloat16 | | | | |
| DataView.prototype.getFloat32 | | | | |
| DataView.prototype.getFloat64 | | | | |
| DataView.prototype.getInt8 | | | | |
| DataView.prototype.getInt16 | | | | |
| DataView.prototype.getInt32 | | | | |
| DataView.prototype.getUint8 | | | | |
| DataView.prototype.getUint16 | | | | |
| DataView.prototype.getUint32 | | | | |
| DataView.prototype.setBigInt64 | | | | |
| DataView.prototype.setBigUint64 | | | | |
| DataView.prototype.setFloat16 | | | | |
| DataView.prototype.setFloat32 | | | | |
| DataView.prototype.setFloat64 | | | | |
| DataView.prototype.setInt8 | | | | |
| DataView.prototype.setInt16 | | | | |
| DataView.prototype.setInt32 | | | | |
| DataView.prototype.setUint8 | | | | |
| DataView.prototype.setUint16 | | | | |
| DataView.prototype.setUint32 | | | | |

#### Atomics (not constructible, namespace)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Atomics[Symbol.toStringTag] | | | | |
| Atomics.add | | | | |
| Atomics.and | | | | |
| Atomics.compareExchange | | | | |
| Atomics.exchange | | | | |
| Atomics.isLockFree | | | | |
| Atomics.load | | | | |
| Atomics.notify | | | | |
| Atomics.or | | | | |
| Atomics.pause | | | | |
| Atomics.store | | | | |
| Atomics.sub | | | | |
| Atomics.wait | | | | |
| Atomics.waitAsync | | | | |
| Atomics.xor | | | | |

#### JSON (not constructible, namespace)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| JSON[Symbol.toStringTag] | | | | |
| JSON.parse | | | | |
| JSON.stringify | | | | |
| JSON.isRawJSON | | | | |
| JSON.rawJSON | | | | |

#### WeakRef

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| WeakRef() (constructor) | | | | |
| WeakRef.prototype.constructor | | | | |
| WeakRef.prototype[Symbol.toStringTag] | | | | |
| WeakRef.prototype.deref | | | | |

#### FinalizationRegistry

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| FinalizationRegistry() (constructor) | | | | |
| FinalizationRegistry.prototype.constructor | | | | |
| FinalizationRegistry.prototype[Symbol.toStringTag] | | | | |
| FinalizationRegistry.prototype.register | | | | |
| FinalizationRegistry.prototype.unregister | | | | |

#### Promise

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Promise() (constructor) | | | | |
| Promise.all | | | | |
| Promise.allSettled | | | | |
| Promise.any | | | | |
| Promise.race | | | | |
| Promise.reject | | | | |
| Promise.resolve | | | | |
| Promise.try | | | | |
| Promise.withResolvers | | | | |
| Promise.allKeyed | | | | |
| Promise.allSettledKeyed | | | | |
| Promise[Symbol.species] | | | | |
| Promise.prototype.constructor | | | | |
| Promise.prototype[Symbol.toStringTag] | | | | |
| Promise.prototype.then | | | | |
| Promise.prototype.catch | | | | |
| Promise.prototype.finally | | | | |

#### GeneratorFunction (not a global; obtained via `(function*(){}).constructor`)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| GeneratorFunction() (constructor, not globally exposed) | | | | |
| GeneratorFunction.prototype.constructor | | | | |
| GeneratorFunction.prototype.prototype | | | | |
| GeneratorFunction.prototype[Symbol.toStringTag] | | | | |

#### Generator (the object returned by calling a generator function; its prototype sits under GeneratorFunction.prototype.prototype)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Generator.prototype.constructor | | | | |
| Generator.prototype[Symbol.toStringTag] | | | | |
| Generator.prototype.next | | | | |
| Generator.prototype.return | | | | |
| Generator.prototype.throw | | | | |
| Generator.prototype[Symbol.iterator] | | | | |

#### AsyncGeneratorFunction (not a global; obtained via `(async function*(){}).constructor`)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| AsyncGeneratorFunction() (constructor, not globally exposed) | | | | |
| AsyncGeneratorFunction.prototype.constructor | | | | |
| AsyncGeneratorFunction.prototype.prototype | | | | |
| AsyncGeneratorFunction.prototype[Symbol.toStringTag] | | | | |

#### AsyncGenerator (the object returned by calling an async generator function)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| AsyncGenerator.prototype.constructor | | | | |
| AsyncGenerator.prototype[Symbol.toStringTag] | | | | |
| AsyncGenerator.prototype.next | | | | |
| AsyncGenerator.prototype.return | | | | |
| AsyncGenerator.prototype.throw | | | | |
| AsyncGenerator.prototype[Symbol.asyncIterator] | | | | |

#### AsyncFunction (not a global; obtained via `(async function(){}).constructor`)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| AsyncFunction() (constructor, not globally exposed) | | | | |
| AsyncFunction.prototype.constructor | | | | |
| AsyncFunction.prototype[Symbol.toStringTag] | | | | |

#### Iterator (common protocol/helper base for built-in iterators, incl. Iterator helper methods)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Iterator() (constructor; abstract, cannot be instantiated directly) | | | | |
| Iterator.from | | | | |
| Iterator.concat | | | | |
| Iterator.zip | | | | |
| Iterator.zipKeyed | | | | |
| Iterator.prototype.constructor | | | | |
| Iterator.prototype[Symbol.toStringTag] | | | | |
| Iterator.prototype[Symbol.iterator] | | | | |
| Iterator.prototype.map | | | | |
| Iterator.prototype.filter | | | | |
| Iterator.prototype.take | | | | |
| Iterator.prototype.drop | | | | |
| Iterator.prototype.flatMap | | | | |
| Iterator.prototype.reduce | | | | |
| Iterator.prototype.toArray | | | | |
| Iterator.prototype.forEach | | | | |
| Iterator.prototype.some | | | | |
| Iterator.prototype.every | | | | |
| Iterator.prototype.find | | | | |
| Iterator.prototype.chunks | | | | |
| Iterator.prototype.windows | | | | |
| Iterator.prototype.join | | | | |
| Iterator.prototype.includes | | | | |
| Iterator.prototype[Symbol.dispose] | | | | |

#### AsyncIterator (common protocol base for built-in async iterators)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| AsyncIterator is not currently exposed as a global constructor | | | | |
| AsyncIterator.prototype[Symbol.asyncIterator] | | | | |
| AsyncIterator.prototype[Symbol.asyncDispose] | | | | |

#### Reflect (not constructible, namespace)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Reflect[Symbol.toStringTag] | | | | |
| Reflect.apply | | | | |
| Reflect.construct | | | | |
| Reflect.defineProperty | | | | |
| Reflect.deleteProperty | | | | |
| Reflect.get | | | | |
| Reflect.getOwnPropertyDescriptor | | | | |
| Reflect.getPrototypeOf | | | | |
| Reflect.has | | | | |
| Reflect.isExtensible | | | | |
| Reflect.ownKeys | | | | |
| Reflect.preventExtensions | | | | |
| Reflect.set | | | | |
| Reflect.setPrototypeOf | | | | |

#### Proxy

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Proxy() (constructor) | | | | |
| Proxy.revocable | | | | |
| Proxy handler trap: get | | | | |
| Proxy handler trap: set | | | | |
| Proxy handler trap: has | | | | |
| Proxy handler trap: deleteProperty | | | | |
| Proxy handler trap: ownKeys | | | | |
| Proxy handler trap: getOwnPropertyDescriptor | | | | |
| Proxy handler trap: defineProperty | | | | |
| Proxy handler trap: preventExtensions | | | | |
| Proxy handler trap: getPrototypeOf | | | | |
| Proxy handler trap: setPrototypeOf | | | | |
| Proxy handler trap: isExtensible | | | | |
| Proxy handler trap: apply | | | | |
| Proxy handler trap: construct | | | | |

### 3d. Intl (ECMA-402)

Grouped per namespace object rather than per member (per task scope).

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| Intl (namespace object itself; `Intl[Symbol.toStringTag]`) | | | | |
| Intl.getCanonicalLocales | | | | |
| Intl.supportedValuesOf | | | | |
| Intl.Collator | | | | |
| Intl.DateTimeFormat | | | | |
| Intl.DisplayNames | | | | |
| Intl.DurationFormat | | | | |
| Intl.ListFormat | | | | |
| Intl.Locale | | | | |
| Intl.NumberFormat | | | | |
| Intl.PluralRules | | | | |
| Intl.RelativeTimeFormat | | | | |
| Intl.Segmenter | | | | |

## Sources

- MDN JavaScript Reference — Statements — https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Statements — accessed 2026-09-23
- MDN JavaScript Reference — Operators — https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators — accessed 2026-09-23
- MDN — import / export statement pages — accessed 2026-09-23
- ECMA-262 current edition, clauses 12.5, 13, 14, 16 — https://tc39.es/ecma262/ — accessed 2026-09-23
- MDN Standard built-in objects index — https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects — accessed 2026-09-23
- MDN per-object pages for every built-in in 3c (Object, Function, Boolean, Symbol, Error, AggregateError, Number, BigInt, Math, Date, String, RegExp, Array, TypedArray, Map, Set, WeakMap, WeakSet, ArrayBuffer, SharedArrayBuffer, DataView, Atomics, JSON, WeakRef, FinalizationRegistry, Promise, GeneratorFunction, Generator, AsyncGeneratorFunction, AsyncGenerator, AsyncFunction, Iterator, AsyncIterator, Reflect, Proxy, Intl) — all accessed 2026-09-23
- Direct spot-checks of current-edition status — https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Object/groupBy and .../Iterator — accessed 2026-09-23 (see gap resolution below)

**Gap flagged during research, and its resolution:** a whole-page fetch of `tc39.es/ecma262`'s table of contents reported `Object.groupBy`, `Map.groupBy`, `Promise.try`, `Array.fromAsync`, the `Iterator.prototype` helper methods, `RegExp.escape`, and the `Uint8Array` base64 methods as absent from the current spec, and labelled the whole document "ECMAScript 2027". Re-checked directly (2026-09-23) against MDN's own spec-link for `Object.groupBy` and `Iterator`: both point at live `sec-*` anchors inside `tc39.es/ecma262` today (`sec-object.groupby`, `sec-iterator-objects`) — the "2027" label is only the next yearly edition's name, not evidence the content is missing from the current living document. The whole-TOC fetch's "not found" results were a summarization miss on a very large single page, not a real absence. Resolution: all of the built-ins flagged as "uncertain" in the original 3c research (Object.groupBy, Map.groupBy, Promise.try/withResolvers/allKeyed/allSettledKeyed, Array.fromAsync, the Iterator helper methods, RegExp.escape, DataView Float16 methods, JSON.isRawJSON/rawJSON, Map/WeakMap getOrInsert/getOrInsertComputed, Atomics.pause, ArrayBuffer resize/transfer family, SharedArrayBuffer growable family, Symbol.dispose/asyncDispose and the matching Iterator/AsyncIterator dispose symbols) are kept as ordinary (non-flagged) rows — they are confirmed current, not merely proposed. The one exception that stays excluded, per MDN's own page for it: AsyncIterator's helper methods (map/filter/take/drop/etc.) are still a separate, unshipped proposal distinct from the synchronous Iterator helpers, so only the two protocol symbols are listed under AsyncIterator.

**Other notes carried from research:** NativeError subtypes (AggregateError, EvalError, RangeError, ReferenceError, SyntaxError, TypeError, URIError) list only their own constructor plus any member unique to the subtype — their `.prototype` otherwise inherits `Error.prototype` unchanged and is not re-listed seven times. The full %TypedArray%.prototype surface is listed once and stated to apply identically to all 11 concrete typed-array constructors rather than repeated 11 times. Generic `Object.prototype`/`Function.prototype` members inherited unchanged by every other built-in (hasOwnProperty, toString, valueOf, apply, call, bind, etc.) are listed once under Object/Function, not re-listed per built-in — only members a built-in owns or overrides get their own row. `import.meta` is intentionally cross-listed in both 3a (as a note against the Import declaration bullet) and 3b (as its substantive row, since it is grammatically a MetaProperty expression, not a Statement) — this is a deliberate cross-listing, not a duplicate. Global `escape()`/`unescape()` (legacy Annex B) were not included among the 13 global functions, matching the task's explicit list.

## Counts

**797 total data rows** (verified by counting table rows in the assembled file — sub-agent self-reported subtotals below are approximate hand counts, kept for orientation): 3a statements ~54, 3b expressions/operators ~99, 3c built-in object members across 37 built-ins, 3d Intl 13 namespace rows.
