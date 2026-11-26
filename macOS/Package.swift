// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Faemyr",
    platforms: [.macOS("14.0")],
    products: [.executable(name: "Faemyr", targets: ["Faemyr"])],
    targets: [
        .systemLibrary(name: "CSQLite"),
        .target(name: "FaemyrCore", dependencies: ["CSQLite"], resources: [.copy("Resources")]),
        .executableTarget(name: "Faemyr", dependencies: ["FaemyrCore"]),
        .executableTarget(name: "FaemyrSelfTest", dependencies: ["FaemyrCore"])
    ]
)
