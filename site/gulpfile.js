var gulp = require('gulp');
var responsive = require('gulp-responsive');


gulp.task('figures', function () {
  return gulp.src([
      './assets/documentation/**/!(thumbnail)*.{png,jpg,jpeg}',
      './assets/projects/**/!(thumbnail)*.{png,jpg,jpeg}',
      './assets/techniques/**/!(thumbnail)*.{png,jpg,jpeg}'
    ]).pipe(responsive({
      '**/*.*': [
        {
          rename: {
            suffix: '-original', // original image (for linking to)
            extname: '.jpg',
          },
        },
        {
          width: 800,
          rename: {
            suffix: '-small',
            extname: '.jpg',
          },
        },
        {
          width: 1600,
          rename: {
            suffix: '-small@2x',
            extname: '.jpg',
          },
        },
        {
          width: 960,
          rename: {
            suffix: '-medium',
            extname: '.jpg',
          },
        },
        {
          width: 1920,
          rename: {
            suffix: '-medium@2x',
            extname: '.jpg',
          },
        },
        {
          width: 1344,
          rename: {
            suffix: '-large',
            extname: '.jpg',
          },
        },
        {
          width: 2688,
          rename: {
            suffix: '-large@2x',
            extname: '.jpg',
          },
        },
        {
          width: 800,
          rename: {
            suffix: '-small',
            extname: '.webp',
          },
        },
        {
          width: 1600,
          rename: {
            suffix: '-small@2x',
            extname: '.webp',
          },
        },
        {
          width: 960,
          rename: {
            suffix: '-medium',
            extname: '.webp',
          },
        },
        {
          width: 1920,
          rename: {
            suffix: '-medium@2x',
            extname: '.webp',
          },
        },
        {
          width: 1344,
          rename: {
            suffix: '-large',
            extname: '.webp',
          },
        },
        {
          width: 2688,
          rename: {
            suffix: '-large@2x',
            extname: '.webp',
          },
        }
      ],
    }, {
      // Global configuration for all images
      // The output quality for JPEG, WebP and TIFF output formats
      quality: 70,
      // Use progressive (interlace) scan for JPEG and PNG output
      progressive: true,
      // Zlib compression level of PNG output format
      compressionLevel: 6,
      // Strip all metadata
      withMetadata: false,
      // Skip sizes that are too big
      withoutEnlargement: true,
      skipOnEnlargement: false,
      errorOnEnlargement: false
    }))
    .pipe(gulp.dest('_site/assets/img/'));
});


// These have different quality and size formats (lower/smaller/singular)
gulp.task('thumbnails', function () {
  return gulp.src([
    './assets/documentation/**/thumbnail.{png,jpg,jpeg}',
    './assets/projects/**/thumbnail.{png,jpg,jpeg}',
    './assets/techniques/**/thumbnail.{png,jpg,jpeg}'
  ]).pipe(responsive({
    '**/*.*': [
      {
        width: 690,
        rename: {
          suffix: '-medium',
          extname: '.jpg',
        },
      },
      {
        width: 690,
        rename: {
          suffix: '-medium',
          extname: '.webp',
        },
      }
    ]
  }, {
    // Global configuration for all images
    // The output quality for JPEG, WebP and TIFF output formats
    quality: 70,
    // Zlib compression level of PNG output format
    compressionLevel: 6,
    // Strip all metadata
    withMetadata: false,
  }))
  .pipe(gulp.dest('_site/assets/img/'));
});


gulp.task('images', gulp.series('thumbnails', 'figures'));
