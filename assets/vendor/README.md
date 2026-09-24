# Bundled libraries

These files are bundled so the viewer works without an internet connection, for
example in the KiRI Windows app. They are unmodified copies of:

| File | Library | Licence |
|---|---|---|
| `jquery-3.6.0.min.js` | [jQuery](https://jquery.com/) 3.6.0 | MIT |
| `bootstrap.min.css`, `bootstrap.bundle.min.js` | [Bootstrap](https://getbootstrap.com/) 4.6.1 (the bundle includes Popper) | MIT |
| `mousetrap.min.js` | [Mousetrap](https://craig.is/killing/mice) 1.6.5 | Apache-2.0 |
| `svg-pan-zoom.min.js` | [svg-pan-zoom](https://github.com/bumbu/svg-pan-zoom) 3.6.1 | BSD-2-Clause |
| `iconify.min.js` | [Iconify](https://iconify.design/) 2.2.1 | MIT |

`kiri-icons.js` holds the data of the icons the viewer uses, taken from the
[Iconify API](https://api.iconify.design). The icons come from these sets:

| Prefix | Icon set | Licence |
|---|---|---|
| `akar-icons` | [Akar Icons](https://github.com/artcoholic/akar-icons) | MIT |
| `bi` | [Bootstrap Icons](https://github.com/twbs/icons) | MIT |
| `bx` | [BoxIcons](https://github.com/box-icons/boxicons) | MIT |
| `carbon` | [Carbon](https://github.com/carbon-design-system/carbon) | Apache-2.0 |
| `codicon` | [Codicons](https://github.com/microsoft/vscode-codicons) by Microsoft | CC BY 4.0 |
| `fa-solid` | [Font Awesome 5 Solid](https://fontawesome.com/) by Dave Gandy | CC BY 4.0 |
| `mdi` | [Material Design Icons](https://github.com/Templarian/MaterialDesign) | Apache-2.0 |
| `teenyicons` | [Teenyicons](https://github.com/teenyicons/teenyicons) | MIT |

To add an icon, fetch `https://api.iconify.design/<prefix>.json?icons=<name>,...`
and add it to `kiri-icons.js`.
